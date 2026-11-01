using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.People.Domain;
using YeaKazi.People.Persistence;

namespace YeaKazi.People.Services;

/// <summary>
/// Bank and branch reference table (Section 15: "bank/branch from reference table"). Kept by the
/// tenant until an approved bank-code source is loaded as rule data (docs/assumptions.md A-27).
/// </summary>
public sealed partial class BankBranchService(PeopleDbContext db, IAccessControl access, AuditTrail audit)
{
    public async Task<IReadOnlyList<BankBranchDto>> ListAsync(bool? includeInactive, CancellationToken ct)
    {
        var read = await access.ScopeAsync(Permissions.PeopleRead, ct);
        if (!read.Granted)
        {
            throw new ForbiddenException();
        }

        var q = db.BankBranches.AsNoTracking();
        if (includeInactive != true)
        {
            q = q.Where(b => b.IsActive);
        }

        return (await q.OrderBy(b => b.BankName).ThenBy(b => b.BranchName).ToListAsync(ct)).Select(Map).ToList();
    }

    public async Task<BankBranchDto> CreateAsync(CreateBankBranchRequest r, CancellationToken ct)
    {
        await RequireTenantWideWriteAsync(ct);
        var bank = r.BankCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var branch = r.BranchCode?.Trim().ToUpperInvariant() ?? string.Empty;
        new Validator()
            .Required("bankCode", bank, 10).Must(bank.Length == 0 || bank.All(char.IsAsciiLetterOrDigit), "bankCode", "Use letters and digits.")
            .Required("bankName", r.BankName, 120)
            .Required("branchCode", branch, 10).Must(branch.Length == 0 || branch.All(char.IsAsciiLetterOrDigit), "branchCode", "Use letters and digits.")
            .Required("branchName", r.BranchName, 120)
            .ThrowIfInvalid();
        if (await db.BankBranches.AnyAsync(b => b.BankCode == bank && b.BranchCode == branch, ct))
        {
            throw new ConflictException("duplicate_bank_branch", "This bank branch is already in the table.");
        }

        var entity = new BankBranch { BankCode = bank, BankName = r.BankName!.Trim(), BranchCode = branch, BranchName = r.BranchName!.Trim() };
        db.BankBranches.Add(entity);
        audit.Record(db, "people.bank_branch.created", "bank_branch", entity.Id, after: AuditSnapshot.Of(entity));
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<BankBranchDto> UpdateAsync(Guid id, UpdateBankBranchRequest r, CancellationToken ct)
    {
        await RequireTenantWideWriteAsync(ct);
        var entity = await db.BankBranches.SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Bank branch");
        if (entity.Version != r.Version)
        {
            throw new ConflictException("concurrency_conflict", "The record was changed by someone else. Reload and try again.");
        }

        new Validator().Required("bankName", r.BankName, 120).Required("branchName", r.BranchName, 120).Optional("reason", r.Reason, 1000).ThrowIfInvalid();
        var before = AuditSnapshot.Of(entity);
        var wasActive = entity.IsActive;
        entity.BankName = r.BankName!.Trim();
        entity.BranchName = r.BranchName!.Trim();
        entity.IsActive = r.IsActive;
        db.Entry(entity).Property(b => b.Version).OriginalValue = r.Version;
        audit.Record(db, $"people.bank_branch.{(wasActive == r.IsActive ? "updated" : r.IsActive ? "reactivated" : "deactivated")}", "bank_branch", entity.Id,
            before, AuditSnapshot.Of(entity), r.Reason);
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    private async Task RequireTenantWideWriteAsync(CancellationToken ct)
    {
        var scope = await access.ScopeAsync(Permissions.PeopleWrite, ct);
        if (!scope.AllLegalEntities)
        {
            throw new ForbiddenException("Changing the bank reference table needs employee administration rights for all employers.");
        }
    }

    private static BankBranchDto Map(BankBranch b) => new(b.Id, b.BankCode, b.BankName, b.BranchCode, b.BranchName, b.IsActive, b.Version);
}
