using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Domain;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Persistence;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Organization.Domain;
using YeaKazi.Organization.Persistence;
using YeaKazi.Organization.Services;

namespace YeaKazi.UnitTests;

public class HierarchyTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    // A <- B <- C (C reports to B, B reports to A)
    private static readonly Dictionary<Guid, Guid?> Parents = new() { [A] = null, [B] = A, [C] = B, [D] = null };

    [Fact]
    public void Detects_direct_and_indirect_cycles()
    {
        Assert.True(Hierarchy.WouldCreateCycle(A, C, Parents));
        Assert.True(Hierarchy.WouldCreateCycle(A, B, Parents));
        Assert.True(Hierarchy.WouldCreateCycle(A, A, Parents));
    }

    [Fact]
    public void Allows_valid_moves()
    {
        Assert.False(Hierarchy.WouldCreateCycle(C, A, Parents));
        Assert.False(Hierarchy.WouldCreateCycle(D, C, Parents));
        Assert.False(Hierarchy.WouldCreateCycle(B, null, Parents));
    }

    [Fact]
    public void Treats_existing_corrupt_cycle_as_unsafe()
    {
        var corrupt = new Dictionary<Guid, Guid?> { [A] = B, [B] = A };
        Assert.True(Hierarchy.WouldCreateCycle(C, A, corrupt));
    }
}

public class KenyanIdentifierTests
{
    [Theory]
    [InlineData("P051234567X", true)]
    [InlineData("A012345678B", true)]
    [InlineData("p051234567x", false)]
    [InlineData("P05123456X", false)]
    [InlineData("X051234567X", false)]
    [InlineData("P0512345678", false)]
    public void Validates_employer_kra_pin(string pin, bool valid) => Assert.Equal(valid, KenyanIdentifiers.IsValidEmployerKraPin(pin));

    [Theory]
    [InlineData("A012345678B", true)]
    [InlineData("P051234567X", false)]
    public void Validates_individual_kra_pin_per_section_15(string pin, bool valid) => Assert.Equal(valid, KenyanIdentifiers.IsValidIndividualKraPin(pin));
}

public class WorkScheduleTests
{
    [Fact]
    public void Overnight_shift_spans_midnight()
    {
        var s = new WorkSchedule { StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(6, 0), BreakMinutes = 30 };
        Assert.True(s.IsOvernight);
        Assert.Equal(450, s.ScheduledMinutesPerDay);
    }

    [Fact]
    public void Day_shift_minutes()
    {
        var s = new WorkSchedule { StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), BreakMinutes = 60 };
        Assert.False(s.IsOvernight);
        Assert.Equal(480, s.ScheduledMinutesPerDay);
    }

    [Theory]
    [InlineData("08:00", "17:00", 60, true)]
    [InlineData("22:00", "06:00", 30, true)]
    [InlineData("8am", "17:00", 60, false)]
    [InlineData("08:00", "08:00", 0, false)]
    [InlineData("08:00", "09:00", 60, false)]
    public void Validates_schedule(string start, string end, int breakMinutes, bool valid)
    {
        var (_, _, v) = OrganizationService.ValidateSchedule(start, end, breakMinutes, [1, 2, 3, 4, 5]);
        if (valid)
        {
            v.ThrowIfInvalid();
        }
        else
        {
            Assert.Throws<ValidationException>(v.ThrowIfInvalid);
        }
    }
}

public class AuditSnapshotTests
{
    private sealed class Sample
    {
        public string Name { get; set; } = "Jane";
        [AuditIgnore] public string Secret { get; set; } = "s3cr3t";
        [AuditMask] public string Email { get; set; } = "jane.doe@acme.co.ke";
        [AuditMask] public string Pin { get; set; } = "A012345678B";
    }

    [Fact]
    public void Drops_ignored_and_masks_sensitive_fields()
    {
        var json = AuditSnapshot.Of(new Sample())!.RootElement;
        Assert.Equal("Jane", json.GetProperty("name").GetString());
        Assert.False(json.TryGetProperty("secret", out _));
        Assert.Equal("j***@acme.co.ke", json.GetProperty("email").GetString());
        Assert.Equal("***78B", json.GetProperty("pin").GetString());
    }
}

/// <summary>The write controls run before any SQL is sent, so no database is needed here.</summary>
public class WriteControlTests
{
    private static OrganizationDbContext Create(RequestContext ctx)
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new OrganizationDbContext(options, ctx);
    }

    [Fact]
    public async Task Write_without_audit_event_is_rejected()
    {
        var ctx = new RequestContext();
        ctx.SetTenantUser(Guid.NewGuid(), Guid.NewGuid(), "Tester");
        await using var db = Create(ctx);
        db.Branches.Add(new Branch { LegalEntityId = Guid.NewGuid(), Code = "B1", Name = "Nairobi" });
        await Assert.ThrowsAsync<AuditIntegrityException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Record_for_another_tenant_is_rejected()
    {
        var ctx = new RequestContext();
        ctx.SetTenantUser(Guid.NewGuid(), Guid.NewGuid(), "Tester");
        await using var db = Create(ctx);
        db.Branches.Add(new Branch { TenantId = Guid.NewGuid(), LegalEntityId = Guid.NewGuid(), Code = "B1", Name = "Nairobi" });
        new AuditTrail(ctx).Record(db, "org.branch.created", "branch", null);
        await Assert.ThrowsAsync<TenantScopeException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Tenant_id_is_taken_from_context_not_input()
    {
        var tenant = Guid.NewGuid();
        var ctx = new RequestContext();
        ctx.SetTenantUser(tenant, Guid.NewGuid(), "Tester");
        await using var db = Create(ctx);
        var branch = new Branch { LegalEntityId = Guid.NewGuid(), Code = "B1", Name = "Nairobi" };
        db.Branches.Add(branch);
        new AuditTrail(ctx).Record(db, "org.branch.created", "branch", branch.Id);
        // SaveChanges sets tenant_id and then fails to connect; the assignment happens first.
        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        Assert.Equal(tenant, branch.TenantId);
    }

    [Fact]
    public async Task Writes_without_tenant_context_are_rejected()
    {
        await using var db = Create(new RequestContext());
        db.Branches.Add(new Branch { LegalEntityId = Guid.NewGuid(), Code = "B1", Name = "Nairobi" });
        await Assert.ThrowsAsync<TenantScopeException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Request_context_tenant_cannot_change()
    {
        var ctx = new RequestContext();
        ctx.SetTenant(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => ctx.SetTenant(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(ctx.EnterPlatformScope);
    }

    [Fact]
    public void Platform_scope_cannot_act_in_a_tenant_outside_provisioning()
    {
        var ctx = new RequestContext();
        ctx.EnterPlatformScope();
        Assert.Throws<InvalidOperationException>(() => ctx.SetTenant(Guid.NewGuid()));
    }
}
