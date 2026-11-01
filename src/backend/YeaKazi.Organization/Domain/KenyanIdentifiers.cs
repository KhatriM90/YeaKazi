using System.Text.RegularExpressions;

namespace YeaKazi.Organization.Domain;

public static partial class KenyanIdentifiers
{
    /// <summary>
    /// Employer KRA PIN. Section 15 gives only the individual pattern (^A\d{9}[A-Z]$); employers
    /// may be companies (P...) or individuals (A...). See docs/assumptions.md A-06.
    /// </summary>
    public static bool IsValidEmployerKraPin(string? pin) => pin is not null && EmployerPin().IsMatch(pin);

    /// <summary>Individual KRA PIN per Section 15 (used from S2 for employees).</summary>
    public static bool IsValidIndividualKraPin(string? pin) => pin is not null && IndividualPin().IsMatch(pin);

    [GeneratedRegex("^[AP]\\d{9}[A-Z]$")]
    private static partial Regex EmployerPin();

    [GeneratedRegex("^A\\d{9}[A-Z]$")]
    private static partial Regex IndividualPin();
}
