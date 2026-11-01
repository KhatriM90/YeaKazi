namespace YeaKazi.BuildingBlocks.Contracts;

/// <summary>Internal interface of the Organization module used by Platform provisioning.</summary>
public interface IOrganizationProvisioning
{
    /// <summary>Creates the tenant's first legal employer inside the caller's shared transaction.</summary>
    Task<Guid> CreateInitialLegalEntityAsync(InitialLegalEntity spec, CancellationToken ct = default);
}

public sealed record InitialLegalEntity(
    string Code,
    string Name,
    string? RegistrationNumber,
    string? KraPin,
    string? NssfEmployerNumber,
    string Timezone);

/// <summary>Read-only lookups other modules may make into Organization.</summary>
public interface IOrganizationDirectory
{
    Task<bool> LegalEntityExistsAsync(Guid legalEntityId, CancellationToken ct = default);
}
