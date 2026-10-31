using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Domain;
using YeaKazi.BuildingBlocks.Outbox;
using YeaKazi.BuildingBlocks.Tenancy;

namespace YeaKazi.BuildingBlocks.Persistence;

/// <summary>
/// Base class for every module DbContext. Enforces, in code, the controls that the
/// database also enforces through row-level security:
///  - tenant_id on new rows comes from the request context, never from input;
///  - tenant_id can never be changed on an existing row;
///  - every unit of work that writes business data also writes an audit record;
///  - query filters restrict every tenant-owned entity to the current tenant.
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    private readonly RequestContext _requestContext;
    private readonly SharedDbConnection? _shared;

    protected ModuleDbContext(DbContextOptions options, RequestContext requestContext, SharedDbConnection? shared)
        : base(options)
    {
        _requestContext = requestContext;
        _shared = shared;
    }

    public RequestContext RequestContext => _requestContext;

    /// <summary>Referenced by the global tenant query filter.</summary>
    protected Guid? CurrentTenantId => _requestContext.TenantId;

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected abstract bool OwnsSharedTables { get; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money is decimal(18,2) everywhere unless a column says otherwise.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        SharedModel.ConfigureAudit(modelBuilder, OwnsSharedTables);
        SharedModel.ConfigureOutbox(modelBuilder, OwnsSharedTables);
        ConfigureModule(modelBuilder);
        ApplyCrossCuttingConventions(modelBuilder);
    }

    protected abstract void ConfigureModule(ModelBuilder modelBuilder);

    private void ApplyCrossCuttingConventions(ModelBuilder modelBuilder)
    {
        var applyFilter = typeof(ModuleDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clr = entityType.ClrType;
            if (typeof(ITenantOwned).IsAssignableFrom(clr) && entityType.BaseType is null)
            {
                applyFilter.MakeGenericMethod(clr).Invoke(this, [modelBuilder]);
            }

            if (typeof(IVersioned).IsAssignableFrom(clr))
            {
                modelBuilder.Entity(clr).Property(nameof(IVersioned.Version)).IsRowVersion();
            }
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder)
        where T : class, ITenantOwned
    {
        Expression<Func<T, bool>> filter = e => CurrentTenantId != null && e.TenantId == CurrentTenantId;
        modelBuilder.Entity<T>().HasQueryFilter(filter);
    }

    /// <summary>Joins the scope's shared transaction, if one is active.</summary>
    public void EnlistInSharedTransaction()
    {
        if (_shared?.CurrentTransaction is { } tx && Database.CurrentTransaction is null)
        {
            Database.UseTransaction(tx);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnlistInSharedTransaction();
        EnforceWriteControls();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnlistInSharedTransaction();
        EnforceWriteControls();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceWriteControls()
    {
        ChangeTracker.DetectChanges();
        var changed = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (changed.Count == 0)
        {
            return;
        }

        foreach (var entry in changed)
        {
            if (entry.Entity is ITenantOwned owned)
            {
                EnforceTenant(entry, owned);
            }

            if (entry.Entity is IAuditRecord && entry.State != EntityState.Added)
            {
                throw new AuditIntegrityException("Audit records are append-only.");
            }
        }

        var businessWrites = changed.Where(e => e.Entity is not IAuditRecord and not IAuditExempt).ToList();
        var auditWrites = changed.Any(e => e.Entity is IAuditRecord && e.State == EntityState.Added);
        if (businessWrites.Count > 0 && !auditWrites)
        {
            var types = string.Join(", ", businessWrites.Select(e => e.Metadata.ClrType.Name).Distinct());
            throw new AuditIntegrityException($"Write without an audit event is not allowed ({types}).");
        }
    }

    private void EnforceTenant(EntityEntry entry, ITenantOwned owned)
    {
        var current = _requestContext.TenantId
            ?? throw new TenantScopeException("No tenant in the current context; tenant-owned data cannot be written.");

        switch (entry.State)
        {
            case EntityState.Added:
                if (owned.TenantId == Guid.Empty)
                {
                    owned.TenantId = current;
                }
                else if (owned.TenantId != current)
                {
                    throw new TenantScopeException("A record cannot be created in another tenant.");
                }

                break;
            case EntityState.Modified:
            case EntityState.Deleted:
                var original = (Guid)entry.Property(nameof(ITenantOwned.TenantId)).OriginalValue!;
                if (original != current || owned.TenantId != current)
                {
                    throw new TenantScopeException("A record of another tenant cannot be changed.");
                }

                break;
        }
    }
}

public sealed class AuditIntegrityException(string message) : InvalidOperationException(message);

public sealed class TenantScopeException(string message) : InvalidOperationException(message);

/// <summary>Applies the tenant session variables whenever EF opens the shared connection.</summary>
public sealed class TenantSessionInterceptor(RequestContext context) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => TenantSession.Apply(connection, context);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        => TenantSession.ApplyAsync(connection, context, null, cancellationToken);
}
