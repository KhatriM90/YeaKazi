namespace YeaKazi.BuildingBlocks.Domain;

/// <summary>Every tenant business record. tenant_id is set from the request context, never from input.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>Records that belong to one legal employer inside a tenant.</summary>
public interface IEmployerScoped : ITenantOwned
{
    Guid LegalEntityId { get; set; }
}

/// <summary>Optimistic concurrency token mapped to PostgreSQL xmin.</summary>
public interface IVersioned
{
    uint Version { get; set; }
}

/// <summary>An audit record. A unit of work that writes business data must also write one of these.</summary>
public interface IAuditRecord
{
}

/// <summary>Infrastructure rows (outbox bookkeeping) that are not business writes.</summary>
public interface IAuditExempt
{
}

/// <summary>Excluded from audit snapshots entirely (secrets, hashes).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute
{
}

/// <summary>Masked in audit snapshots (restricted personal or statutory data).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditMaskAttribute : Attribute
{
}
