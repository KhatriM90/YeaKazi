using System.Text.Json;
using YeaKazi.BuildingBlocks.Contracts;

namespace YeaKazi.Organization.Domain;

public sealed record GeneratedHoliday(DateOnly Date, string Code, string Name, string RuleVersion, DateOnly? MovedFrom);

/// <summary>
/// Turns the effective-dated public-holiday rule (rules/ke/calendar) into dates for one year.
/// No date or rule is hard-coded here: fixed dates and the Sunday rule both come from the rule payload.
/// </summary>
public static class PublicHolidays
{
    public const string SundayRuleNextFreeDay = "next_day_not_public_holiday";

    /// <param name="otherPublicHolidays">Public holidays already known for the year (e.g. Idd-ul-Fitr added by an administrator); a moved holiday never lands on one.</param>
    public static IReadOnlyList<GeneratedHoliday> Generate(int year, IReadOnlyList<RuleVersionData> versions, IReadOnlySet<DateOnly> otherPublicHolidays)
    {
        var scheduled = new List<(DateOnly Date, string Code, string Name, RuleVersionData Rule)>();
        foreach (var version in versions)
        {
            if (!version.Payload.TryGetProperty("fixedDates", out var fixedDates))
            {
                continue;
            }

            foreach (var item in fixedDates.EnumerateArray())
            {
                var month = item.GetProperty("month").GetInt32();
                var day = item.GetProperty("day").GetInt32();
                if (day > DateTime.DaysInMonth(year, month))
                {
                    continue;
                }

                var date = new DateOnly(year, month, day);
                if (version.AppliesOn(date))
                {
                    scheduled.Add((date, item.GetProperty("code").GetString()!, item.GetProperty("name").GetString()!, version));
                }
            }
        }

        scheduled.Sort((a, b) => a.Date.CompareTo(b.Date));
        var taken = new HashSet<DateOnly>(otherPublicHolidays);
        foreach (var s in scheduled)
        {
            taken.Add(s.Date);
        }

        var result = new List<GeneratedHoliday>();
        foreach (var s in scheduled)
        {
            var label = $"{s.Rule.Jurisdiction} {s.Rule.RuleCode} {s.Rule.Version}";
            if (s.Date.DayOfWeek == DayOfWeek.Sunday && SundayRule(s.Rule) == SundayRuleNextFreeDay)
            {
                // The Sunday ceases to be a public holiday; the first following day that is not one becomes it.
                var moved = s.Date.AddDays(1);
                while (taken.Contains(moved))
                {
                    moved = moved.AddDays(1);
                }

                taken.Remove(s.Date);
                taken.Add(moved);
                result.Add(new GeneratedHoliday(moved, s.Code, s.Name, label, s.Date));
            }
            else
            {
                result.Add(new GeneratedHoliday(s.Date, s.Code, s.Name, label, null));
            }
        }

        return result.OrderBy(r => r.Date).ToList();
    }

    private static string? SundayRule(RuleVersionData rule)
        => rule.Payload.TryGetProperty("sundayRule", out var r) && r.ValueKind == JsonValueKind.Object && r.TryGetProperty("kind", out var k)
            ? k.GetString()
            : null;

    public static string DisplayName(GeneratedHoliday h)
        => h.MovedFrom is { } from ? $"{h.Name} (moved from Sunday {from:d MMM})" : h.Name;
}
