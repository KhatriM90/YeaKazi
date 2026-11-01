using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Contracts;
using YeaKazi.Platform.Persistence;

namespace YeaKazi.Platform.Rules;

internal sealed class RuleCatalog(PlatformDbContext db) : IRuleCatalog
{
    public async Task<IReadOnlyList<RuleVersionData>> VersionsAsync(string jurisdiction, string ruleCode, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await db.RuleVersions.AsNoTracking()
            .Where(r => r.Jurisdiction == jurisdiction && r.RuleCode == ruleCode
                        && r.EffectiveFrom <= to && (r.EffectiveTo == null || r.EffectiveTo >= from))
            .OrderBy(r => r.EffectiveFrom)
            .ToListAsync(ct);
        return rows.Select(r => new RuleVersionData(r.Jurisdiction, r.RuleCode, r.VersionLabel, r.EffectiveFrom, r.EffectiveTo,
            r.AuthoritySource, r.Status, r.Payload.RootElement.Clone())).ToList();
    }
}
