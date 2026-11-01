using System.Text.Json;

namespace YeaKazi.BuildingBlocks.Contracts;

/// <summary>An effective-dated statutory rule version loaded from /rules (Section 13).</summary>
public sealed record RuleVersionData(
    string Jurisdiction,
    string RuleCode,
    string Version,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string AuthoritySource,
    string Status,
    JsonElement Payload)
{
    public bool AppliesOn(DateOnly date) => date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);
}

/// <summary>Read-only access to rule versions. Implemented by the Platform module.</summary>
public interface IRuleCatalog
{
    /// <summary>All versions of a rule whose effective period overlaps [from, to], ordered by start date.</summary>
    Task<IReadOnlyList<RuleVersionData>> VersionsAsync(string jurisdiction, string ruleCode, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public static class RuleCodes
{
    public const string Kenya = "KE";
    public const string PublicHolidays = "calendar.public_holidays";
}
