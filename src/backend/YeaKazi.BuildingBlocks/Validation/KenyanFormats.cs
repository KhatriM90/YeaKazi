using System.Globalization;
using System.Text.RegularExpressions;

namespace YeaKazi.BuildingBlocks.Validation;

/// <summary>Format checks from the Section 15 data dictionary. Format only: no KRA, NSSF, SHA or IPRS lookups.</summary>
public static partial class KenyanFormats
{
    /// <summary>Individual KRA PIN, ^A\d{9}[A-Z]$, uppercase.</summary>
    public static bool IsIndividualKraPin(string? pin) => pin is not null && IndividualPin().IsMatch(pin);

    /// <summary>Employer KRA PIN: companies start with P, individuals with A (docs/assumptions.md A-06).</summary>
    public static bool IsEmployerKraPin(string? pin) => pin is not null && EmployerPin().IsMatch(pin);

    /// <summary>National ID: 6 to 9 digits.</summary>
    public static bool IsNationalId(string? value) => value is not null && NationalId().IsMatch(value);

    /// <summary>Passport number: letters and digits only, at most 20 (field size).</summary>
    public static bool IsPassport(string? value) => value is not null && Passport().IsMatch(value);

    /// <summary>NSSF number: digits, at most 20.</summary>
    public static bool IsNssfNumber(string? value) => value is not null && Nssf().IsMatch(value);

    /// <summary>
    /// Normalizes a Kenyan mobile number to E.164 (+2547XXXXXXXX or +2541XXXXXXXX). Accepts the
    /// E.164 form, the same without '+', and the local 07/01 forms; spaces and dashes are ignored.
    /// Returns null when the number is not a Kenyan mobile number.
    /// </summary>
    public static string? NormalizeMobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Trim().Where(c => c is not (' ' or '-' or '(' or ')')).ToArray());
        if (digits.StartsWith('0') && digits.Length == 10)
        {
            digits = "+254" + digits[1..];
        }
        else if (digits.StartsWith("254", StringComparison.Ordinal))
        {
            digits = "+" + digits;
        }

        return Mobile().IsMatch(digits) ? digits : null;
    }

    public static bool IsEmail(string? value)
        => value is not null && value.Length <= 254 && Email().IsMatch(value);

    /// <summary>Business dates: ISO yyyy-MM-dd, or dd/MM/yyyy as spreadsheets in Kenya usually write them.</summary>
    public static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateOnly.TryParseExact(value.Trim(), ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    [GeneratedRegex("^A\\d{9}[A-Z]$")]
    private static partial Regex IndividualPin();

    [GeneratedRegex("^[AP]\\d{9}[A-Z]$")]
    private static partial Regex EmployerPin();

    [GeneratedRegex("^\\d{6,9}$")]
    private static partial Regex NationalId();

    [GeneratedRegex("^[A-Z0-9]{1,20}$")]
    private static partial Regex Passport();

    [GeneratedRegex("^\\d{1,20}$")]
    private static partial Regex Nssf();

    [GeneratedRegex("^\\+254[17]\\d{8}$")]
    private static partial Regex Mobile();

    [GeneratedRegex("^[^@\\s]+@[^@\\s]+\\.[^@\\s.]+$")]
    private static partial Regex Email();
}
