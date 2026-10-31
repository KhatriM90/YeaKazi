namespace YeaKazi.BuildingBlocks.Tenancy;

/// <summary>
/// Per-request (or per-job) execution context. The tenant is resolved from the
/// authenticated session (JWT claims) or, for platform provisioning, from the
/// tenant that the platform service has just created. It is never taken from a
/// request body.
/// </summary>
public sealed class RequestContext
{
    public Guid? TenantId { get; private set; }

    /// <summary>True when the caller is a platform operator (SaaS console).</summary>
    public bool IsPlatformScope { get; private set; }

    /// <summary>True only while the platform provisioning service seeds a new tenant.</summary>
    public bool IsProvisioning { get; private set; }

    public Guid? ActorUserId { get; private set; }
    public string? ActorDisplay { get; private set; }
    public Guid? DelegatedActorUserId { get; private set; }
    public string ActorKind { get; private set; } = ActorKinds.Anonymous;

    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public string? ClientIp { get; set; }

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
        }

        if (TenantId is not null && TenantId != tenantId)
        {
            throw new InvalidOperationException("The tenant of a request cannot be changed once set.");
        }

        if (IsPlatformScope && !IsProvisioning)
        {
            throw new InvalidOperationException("Platform scope cannot act inside a tenant except during provisioning.");
        }

        TenantId = tenantId;
    }

    public void SetTenantUser(Guid tenantId, Guid userId, string display)
    {
        SetTenant(tenantId);
        ActorUserId = userId;
        ActorDisplay = display;
        ActorKind = ActorKinds.TenantUser;
    }

    public void EnterPlatformScope()
    {
        if (TenantId is not null)
        {
            throw new InvalidOperationException("A tenant request cannot enter platform scope.");
        }

        IsPlatformScope = true;
    }

    public void SetPlatformUser(Guid platformUserId, string display)
    {
        EnterPlatformScope();
        ActorUserId = platformUserId;
        ActorDisplay = display;
        ActorKind = ActorKinds.PlatformUser;
    }

    public void SetSystemActor(string display)
    {
        ActorUserId = null;
        ActorDisplay = display;
        ActorKind = ActorKinds.System;
    }

    /// <summary>
    /// Lets the platform provisioning service write the seed records of a tenant it
    /// has just created, inside the provisioning transaction only.
    /// </summary>
    public void BeginProvisioning(Guid newTenantId)
    {
        if (!IsPlatformScope)
        {
            throw new InvalidOperationException("Only platform scope can provision a tenant.");
        }

        if (TenantId is not null)
        {
            throw new InvalidOperationException("Provisioning has already started for another tenant.");
        }

        IsProvisioning = true;
        TenantId = newTenantId;
    }

    public void EndProvisioning()
    {
        IsProvisioning = false;
        TenantId = null;
    }
}

public static class ActorKinds
{
    public const string Anonymous = "anonymous";
    public const string TenantUser = "tenant_user";
    public const string PlatformUser = "platform_user";
    public const string System = "system";
}
