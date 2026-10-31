namespace YeaKazi.BuildingBlocks.Security;

/// <summary>Permission checks for the current actor, optionally scoped to a legal employer.</summary>
public interface IAccessControl
{
    /// <summary>Throws ForbiddenException unless the actor holds the permission (for the employer, if given).</summary>
    Task RequireAsync(string permission, Guid? legalEntityId = null, CancellationToken ct = default);

    Task<PermissionScope> ScopeAsync(string permission, CancellationToken ct = default);
}

/// <summary>Where a permission applies: nowhere, every employer of the tenant, or a set of employers.</summary>
public sealed record PermissionScope(bool AllLegalEntities, IReadOnlySet<Guid> LegalEntityIds)
{
    public static readonly PermissionScope None = new(false, new HashSet<Guid>());

    public bool Granted => AllLegalEntities || LegalEntityIds.Count > 0;

    public bool Allows(Guid legalEntityId) => AllLegalEntities || LegalEntityIds.Contains(legalEntityId);
}
