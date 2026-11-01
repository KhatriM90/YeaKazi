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

public sealed record LegalEntityInfo(Guid Id, string Code, string Name, string Timezone, bool IsActive);

public sealed record OrgRef(Guid Id, string Code, string Name, bool IsActive);

/// <summary>Organization records of one employer, keyed by id and by (uppercase) code. Used by People to validate assignments.</summary>
public sealed class OrgReferenceData
{
    public required IReadOnlyDictionary<string, IReadOnlyList<OrgRef>> Kinds { get; init; }

    public const string Departments = "department";
    public const string Positions = "position";
    public const string Branches = "branch";
    public const string CostCenters = "cost_center";
    public const string Grades = "grade";
    public const string WorkSchedules = "work_schedule";

    public OrgRef? ById(string kind, Guid id) => Kinds.TryGetValue(kind, out var list) ? list.FirstOrDefault(r => r.Id == id) : null;

    public OrgRef? ByCode(string kind, string code) => Kinds.TryGetValue(kind, out var list)
        ? list.FirstOrDefault(r => string.Equals(r.Code, code.Trim(), StringComparison.OrdinalIgnoreCase))
        : null;
}

/// <summary>Read-only lookups for the People module.</summary>
public interface IOrganizationReferences
{
    Task<LegalEntityInfo?> GetLegalEntityAsync(Guid legalEntityId, CancellationToken ct = default);

    Task<IReadOnlyList<LegalEntityInfo>> ListLegalEntitiesAsync(CancellationToken ct = default);

    Task<OrgReferenceData> GetReferenceDataAsync(Guid legalEntityId, CancellationToken ct = default);
}
