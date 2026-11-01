using System.Text.Json;
using YeaKazi.BuildingBlocks.Contracts;
using YeaKazi.BuildingBlocks.Validation;
using YeaKazi.Organization.Domain;
using YeaKazi.People.Domain;
using YeaKazi.People.Services;
using YeaKazi.Platform.Auth;

namespace YeaKazi.UnitTests;

public class PublicHolidayRuleTests
{
    private static readonly RuleVersionData Rule = Load();

    private static RuleVersionData Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "YeaKazi.sln")))
        {
            dir = dir.Parent;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.FullName, "rules", "ke", "calendar", "public-holidays.v2026.1.json")));
        var root = doc.RootElement;
        return new RuleVersionData("KE", RuleCodes.PublicHolidays, root.GetProperty("version").GetString()!,
            DateOnly.Parse(root.GetProperty("effectiveFrom").GetString()!), null, "Public Holidays Act", "seed", root.GetProperty("payload").Clone());
    }

    private static Dictionary<string, GeneratedHoliday> For(int year, params DateOnly[] others)
        => PublicHolidays.Generate(year, [Rule], others.ToHashSet()).ToDictionary(h => h.Code);

    [Fact]
    public void Generates_the_eight_fixed_dates_from_the_rule_file()
    {
        var h = For(2026);
        Assert.Equal(8, h.Count);
        Assert.Equal(new DateOnly(2026, 6, 1), h["madaraka_day"].Date);
        Assert.Equal(new DateOnly(2026, 10, 10), h["mazingira_day"].Date);
        Assert.All(h.Values, x => Assert.Null(x.MovedFrom));
    }

    [Fact]
    public void A_sunday_holiday_moves_to_the_next_day_that_is_not_a_public_holiday()
    {
        // 2033: Christmas falls on a Sunday and Boxing Day is Monday, so Christmas moves to Tuesday 27 Dec.
        var h = For(2033);
        Assert.Equal(new DateOnly(2033, 12, 26), h["boxing_day"].Date);
        Assert.Equal(new DateOnly(2033, 12, 27), h["christmas_day"].Date);
        Assert.Equal(new DateOnly(2033, 12, 25), h["christmas_day"].MovedFrom);
        Assert.DoesNotContain(h.Values, x => x.Date == new DateOnly(2033, 12, 25));
        Assert.Contains("moved from Sunday", PublicHolidays.DisplayName(h["christmas_day"]));

        // 2027: Boxing Day on Sunday moves to Monday 27 Dec.
        Assert.Equal(new DateOnly(2027, 12, 27), For(2027)["boxing_day"].Date);
    }

    [Fact]
    public void A_moved_holiday_skips_other_public_holidays_already_on_the_calendar()
    {
        // 2027 Mazingira Day is Sunday 10 Oct; if 11 Oct were already a gazetted holiday it moves to 12 Oct.
        var h = For(2027, new DateOnly(2027, 10, 11));
        Assert.Equal(new DateOnly(2027, 10, 12), h["mazingira_day"].Date);
    }

    [Fact]
    public void Years_before_the_rule_takes_effect_generate_nothing()
        => Assert.Empty(PublicHolidays.Generate(2025, [Rule], new HashSet<DateOnly>()));
}

public class KenyanFormatTests
{
    [Theory]
    [InlineData("0712345678", "+254712345678")]
    [InlineData("0112345678", "+254112345678")]
    [InlineData("254712345678", "+254712345678")]
    [InlineData("+254 712 345 678", "+254712345678")]
    [InlineData("12345", null)]
    [InlineData("0812345678", null)]
    public void Normalizes_kenyan_mobiles(string input, string? expected) => Assert.Equal(expected, KenyanFormats.NormalizeMobile(input));

    [Fact]
    public void Validates_identifiers()
    {
        Assert.True(KenyanFormats.IsIndividualKraPin("A123456789Z"));
        Assert.False(KenyanFormats.IsIndividualKraPin("P051234567X"));
        Assert.True(KenyanFormats.IsNationalId("12345678"));
        Assert.False(KenyanFormats.IsNationalId("12345"));
        Assert.False(KenyanFormats.IsNationalId("1234567890"));
    }

    [Theory]
    [InlineData("2026-03-01")]
    [InlineData("01/03/2026")]
    [InlineData("1/3/2026")]
    public void Parses_iso_and_day_first_dates(string value)
    {
        Assert.True(KenyanFormats.TryParseDate(value, out var d));
        Assert.Equal(new DateOnly(2026, 3, 1), d);
    }

    [Fact]
    public void Rejects_ambiguous_or_invalid_dates() => Assert.False(KenyanFormats.TryParseDate("31/31/2020", out _));
}

public class EmployeeRuleTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    [Theory]
    [InlineData("casual", "monthly", false)]
    [InlineData("casual", "daily", true)]
    [InlineData("piece_rate", "piece_rate", true)]
    [InlineData("piece_rate", "daily", false)]
    [InlineData("permanent", "piece_rate", false)]
    [InlineData("contractor", "piece_rate", true)]
    public void Worker_category_limits_the_pay_basis(string category, string basis, bool ok)
    {
        var e = new FieldErrors();
        EmployeeRules.NormalizeAssignment(new AssignmentInput(category, basis, null, null, null, null, null, null, null, null, null, null), Today, e);
        Assert.Equal(ok, e.Items.Count == 0);
    }

    [Fact]
    public void Fixed_term_needs_an_end_date_and_probation_an_end_of_probation()
    {
        var e = new FieldErrors();
        EmployeeRules.NormalizeAssignment(new AssignmentInput("fixed_term", "monthly", null, null, null, null, null, null, null, null, null, null), Today, e);
        Assert.Contains(e.Items, x => x.Field == "assignment.contractEndDate");
        var p = new FieldErrors();
        EmployeeRules.NormalizeAssignment(new AssignmentInput("probation", "monthly", null, null, null, null, null, null, null, null, null, null), Today, p);
        Assert.Contains(p.Items, x => x.Field == "assignment.probationEndDate");
    }

    [Fact]
    public void Non_citizens_need_a_passport_and_an_unexpired_permit()
    {
        var e = new FieldErrors();
        EmployeeRules.NormalizeIdentifiers(new IdentifiersInput(null, "PA123", "WP1", Today.AddDays(-1), "A123456789Z", null, null, null, null, null), "non_kenyan", Today, e);
        Assert.Contains(e.Items, x => x.Field == "identifiers.workPermitExpiry");
        var ok = new FieldErrors();
        var ids = EmployeeRules.NormalizeIdentifiers(new IdentifiersInput(null, "pa123", "WP1", Today.AddDays(90), "a123456789z", null, null, null, null, null), "non_kenyan", Today, ok);
        Assert.Empty(ok.Items);
        Assert.Equal("PA123", ids!.PassportNo);
        Assert.Equal("A123456789Z", ids.KraPin);
    }

    [Fact]
    public void Kenyans_need_a_national_id_or_passport_and_a_kra_pin()
    {
        var e = new FieldErrors();
        EmployeeRules.NormalizeIdentifiers(new IdentifiersInput(null, null, null, null, null, null, null, null, null, null), "kenyan", Today, e);
        Assert.Contains(e.Items, x => x.Field == "identifiers.nationalId");
        Assert.Contains(e.Items, x => x.Field == "identifiers.kraPin");
    }

    [Fact]
    public void Under_18_is_allowed_only_for_interns_with_guardian_consent()
    {
        var minor = new PersonalInput("A", null, "B", new DateOnly(2010, 1, 1), "female", "kenyan", "resident", "0712345678", "a@b.co.ke", null, true);
        var e = new FieldErrors();
        EmployeeRules.NormalizePersonal(minor, Today, "permanent", e);
        Assert.Contains(e.Items, x => x.Field == "personal.dateOfBirth");
        var intern = new FieldErrors();
        EmployeeRules.NormalizePersonal(minor, Today, "intern", intern);
        Assert.Empty(intern.Items);
    }

    [Theory]
    [InlineData(-1, "expired")]
    [InlineData(5, "7_days")]
    [InlineData(25, "30_days")]
    [InlineData(50, "60_days")]
    [InlineData(120, null)]
    public void Permit_alerts_follow_the_60_30_7_day_windows(int days, string? alert)
        => Assert.Equal(alert, EmployeeRules.PermitAlert(Today.AddDays(days), Today));
}

public class HistoryTests
{
    private static readonly Guid Ep1 = Guid.NewGuid(), Ep2 = Guid.NewGuid();

    private static readonly List<EmploymentEpisode> Episodes =
    [
        new() { Id = Ep1, Sequence = 1, HireDate = new(2020, 1, 1), TerminationDate = new(2024, 12, 31) },
        new() { Id = Ep2, Sequence = 2, HireDate = new(2026, 3, 1) },
    ];

    private static readonly List<AssignmentVersion> Versions =
    [
        new() { EpisodeId = Ep1, EffectiveFrom = new(2020, 1, 1), JobTitle = "Clerk" },
        new() { EpisodeId = Ep1, EffectiveFrom = new(2022, 7, 1), JobTitle = "Wrong", IsCancelled = true },
        new() { EpisodeId = Ep1, EffectiveFrom = new(2022, 7, 1), JobTitle = "Officer" },
        new() { EpisodeId = Ep2, EffectiveFrom = new(2026, 3, 1), JobTitle = "Lead", ChangeType = ChangeTypes.Rehire },
        new() { EpisodeId = Ep2, EffectiveFrom = new(2026, 9, 1), JobTitle = "Lead", ChangeType = ChangeTypes.Suspension, Status = AssignmentStatuses.Suspended },
    ];

    [Fact]
    public void Reads_the_version_in_force_on_any_date_ignoring_cancelled_ones()
    {
        Assert.Equal("Clerk", History.AssignmentOn(Versions, Ep1, new(2022, 6, 30))!.JobTitle);
        Assert.Equal("Officer", History.AssignmentOn(Versions, Ep1, new(2022, 7, 1))!.JobTitle);
        Assert.Equal(new DateOnly(2022, 6, 30), History.EffectiveTo(Versions, Versions[0], Episodes[0]));
        Assert.Equal(new DateOnly(2024, 12, 31), History.EffectiveTo(Versions, Versions[2], Episodes[0]));
    }

    [Fact]
    public void Status_follows_episodes_and_suspensions()
    {
        Assert.Equal(EmployeeStatuses.Active, History.StatusOn(Episodes, Versions, new(2023, 1, 1)));
        // Between employments with a rehire already booked, the person shows as pending start.
        Assert.Equal(EmployeeStatuses.PendingStart, History.StatusOn(Episodes, Versions, new(2025, 6, 1)));
        Assert.Equal(EmployeeStatuses.Terminated, History.StatusOn([Episodes[0]], Versions, new(2025, 6, 1)));
        Assert.Equal(EmployeeStatuses.Active, History.StatusOn(Episodes, Versions, new(2026, 5, 1)));
        Assert.Equal(EmployeeStatuses.Suspended, History.StatusOn(Episodes, Versions, new(2026, 10, 1)));
        Assert.Equal(EmployeeStatuses.PendingStart, History.StatusOn([new() { HireDate = new(2027, 1, 1) }], [], new(2026, 11, 1)));
    }

    [Fact]
    public void Detects_manager_loops()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var managers = new Dictionary<Guid, Guid?> { [a] = null, [b] = a, [c] = b };
        Assert.True(History.WouldCreateManagerCycle(a, c, managers));
        Assert.True(History.WouldCreateManagerCycle(a, a, managers));
        Assert.False(History.WouldCreateManagerCycle(c, a, managers));
    }
}

public class CsvTests
{
    [Fact]
    public void Parses_quotes_commas_newlines_and_bom()
    {
        var rows = Csv.Parse("﻿a,b,c\r\n\"x, y\",\"he said \"\"hi\"\"\",\"line1\nline2\"\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal("x, y", rows[1][0]);
        Assert.Equal("he said \"hi\"", rows[1][1]);
        Assert.Equal("line1\nline2", rows[1][2]);
    }

    [Fact]
    public void Writing_neutralises_spreadsheet_formulas()
    {
        var text = Csv.Write([["=HYPERLINK(\"x\")", "+1", "ok"]]);
        Assert.DoesNotContain(",=", "," + text.Split(',')[0]);
        Assert.StartsWith("\"'=", text);
    }

    [Theory]
    [InlineData("kraPin", "kra_pin")]
    [InlineData("dateOfBirth", "date_of_birth")]
    [InlineData("contacts[0].phone", "emergency_contact_phone")]
    [InlineData("contacts[1].name", "emergency_contact2_name")]
    public void Maps_rule_fields_to_import_columns(string field, string column) => Assert.Equal(column, ImportService.ColumnFor(field));

    [Fact]
    public void Every_import_column_is_unique_and_the_template_matches()
    {
        Assert.Equal(ImportService.Columns.Count, ImportService.Columns.Select(c => c.Name).Distinct().Count());
        Assert.Equal(ImportService.Columns.Select(c => c.Name), Csv.Parse(ImportService.TemplateCsv())[0]);
    }
}

public class RecoveryCodeTests
{
    [Fact]
    public void Codes_normalise_case_spaces_and_dashes_and_hash_per_user()
    {
        var user = Guid.NewGuid();
        Assert.Equal(MfaRecoveryService.Normalize("abcde-fghjk"), MfaRecoveryService.Normalize(" ABCDE FGHJK "));
        Assert.Equal(MfaRecoveryService.Hash(user, "ABCDE-FGHJK"), MfaRecoveryService.Hash(user, "abcdefghjk"));
        Assert.NotEqual(MfaRecoveryService.Hash(user, "ABCDE-FGHJK"), MfaRecoveryService.Hash(Guid.NewGuid(), "ABCDE-FGHJK"));
    }
}
