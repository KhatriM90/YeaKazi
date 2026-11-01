using System.Text.RegularExpressions;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Validation;

namespace YeaKazi.People.Domain;

/// <summary>Collects field errors; used by the API (one ValidationException) and the import (row errors).</summary>
public sealed class FieldErrors
{
    public List<FieldError> Items { get; } = [];

    public bool Any => Items.Count > 0;

    public void Add(string field, string message) => Items.Add(new FieldError(field, message));

    public void ThrowIfAny()
    {
        if (Any)
        {
            throw new ValidationException(Items.GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()));
        }
    }
}

/// <summary>
/// Section 15 data-dictionary rules for the employee master (EMP-01, EMP-03, EMP-04). Pure
/// functions: the same rules run for a single hire and for every import row.
/// </summary>
public static partial class EmployeeRules
{
    public const int AdultAge = 18;

    public static string? NormalizeEmployeeNumber(string? value, FieldErrors e, string field = "employeeNumber")
    {
        var v = Clean(value)?.ToUpperInvariant();
        if (v is null)
        {
            e.Add(field, "Employee number is required.");
        }
        else if (v.Length > 20 || !EmployeeNumber().IsMatch(v))
        {
            e.Add(field, "Use up to 20 letters, digits, '-', '/' or '_'.");
        }

        return v;
    }

    public static Personal? NormalizePersonal(PersonalInput? p, DateOnly hireDate, string? workerCategory, FieldErrors e, string prefix = "personal.")
    {
        if (p is null)
        {
            e.Add(prefix.TrimEnd('.'), "Personal details are required.");
            return null;
        }

        var start = e.Items.Count;
        var first = Name(p.FirstName, prefix + "firstName", true, e);
        var middle = Name(p.MiddleName, prefix + "middleName", false, e);
        var last = Name(p.LastName, prefix + "lastName", true, e);

        if (p.DateOfBirth is not { } dob)
        {
            e.Add(prefix + "dateOfBirth", "Date of birth is required.");
        }
        else if (dob >= hireDate)
        {
            e.Add(prefix + "dateOfBirth", "Date of birth must be before the hire date.");
        }
        else if (AgeOn(dob, hireDate) < AdultAge
                 && !(workerCategory == WorkerCategories.Intern && p.InternGuardianConsent))
        {
            e.Add(prefix + "dateOfBirth", "Employees must be 18 or older, unless hired as an intern/attaché with guardian consent.");
        }

        var gender = Code(p.Gender, Genders.All, prefix + "gender", "female, male or intersex", e);
        var citizenship = Code(p.Citizenship, Citizenships.All, prefix + "citizenship", "kenyan or non_kenyan", e);
        var residency = Code(p.ResidencyStatus, ResidencyStatuses.All, prefix + "residencyStatus", "resident or non_resident", e);

        var phone = KenyanFormats.NormalizeMobile(p.PhoneMobile);
        if (phone is null)
        {
            e.Add(prefix + "phoneMobile", string.IsNullOrWhiteSpace(p.PhoneMobile)
                ? "Mobile phone is required."
                : "Enter a Kenyan mobile number, for example +254712345678 or 0712345678.");
        }

        var emailWork = Clean(p.EmailWork);
        var emailPersonal = Clean(p.EmailPersonal);
        if (emailWork is null && emailPersonal is null)
        {
            e.Add(prefix + "emailWork", "Give a work or personal email address.");
        }

        if (emailWork is not null && !KenyanFormats.IsEmail(emailWork))
        {
            e.Add(prefix + "emailWork", "Enter a valid email address.");
        }

        if (emailPersonal is not null && !KenyanFormats.IsEmail(emailPersonal))
        {
            e.Add(prefix + "emailPersonal", "Enter a valid email address.");
        }

        if (e.Items.Count > start)
        {
            return null;
        }

        return new Personal(first!, middle, last!, p.DateOfBirth!.Value, gender!, citizenship!, residency!, phone!, emailWork, emailPersonal,
            p.InternGuardianConsent);
    }

    public static Identifiers? NormalizeIdentifiers(IdentifiersInput? i, string? citizenship, DateOnly today, FieldErrors e, string prefix = "identifiers.")
    {
        if (i is null)
        {
            e.Add(prefix.TrimEnd('.'), "Identifiers are required.");
            return null;
        }

        var start = e.Items.Count;
        var nationalId = Clean(i.NationalId);
        var passport = Clean(i.PassportNo)?.ToUpperInvariant();
        var permit = Clean(i.WorkPermitNo)?.ToUpperInvariant();
        var kra = Clean(i.KraPin)?.ToUpperInvariant();
        var nssf = Clean(i.NssfNo);
        var sha = Clean(i.ShaNo)?.ToUpperInvariant();
        var helb = Clean(i.HelbRef)?.ToUpperInvariant();
        var disability = Clean(i.DisabilityCertNo)?.ToUpperInvariant();

        if (nationalId is null && passport is null)
        {
            e.Add(prefix + "nationalId", "Give a National ID or a passport number.");
        }

        if (nationalId is not null && !KenyanFormats.IsNationalId(nationalId))
        {
            e.Add(prefix + "nationalId", "National ID must be 6 to 9 digits.");
        }

        if (passport is not null && !KenyanFormats.IsPassport(passport))
        {
            e.Add(prefix + "passportNo", "Passport number must be letters and digits only (up to 20).");
        }

        if (citizenship == Citizenships.NonKenyan)
        {
            if (passport is null)
            {
                e.Add(prefix + "passportNo", "Non-citizens need a passport number.");
            }

            if (permit is null)
            {
                e.Add(prefix + "workPermitNo", "Non-citizens need a work permit number.");
            }

            if (i.WorkPermitExpiry is null)
            {
                e.Add(prefix + "workPermitExpiry", "Non-citizens need the work permit expiry date.");
            }
            else if (i.WorkPermitExpiry < today)
            {
                e.Add(prefix + "workPermitExpiry", "The work permit has expired.");
            }
        }

        if (permit is not null && permit.Length > 20)
        {
            e.Add(prefix + "workPermitNo", "Must be at most 20 characters.");
        }

        if (kra is null)
        {
            e.Add(prefix + "kraPin", "KRA PIN is required.");
        }
        else if (!KenyanFormats.IsIndividualKraPin(kra))
        {
            e.Add(prefix + "kraPin", "Enter an individual KRA PIN, for example A123456789Z.");
        }

        if (nssf is not null && !KenyanFormats.IsNssfNumber(nssf))
        {
            e.Add(prefix + "nssfNo", "NSSF number must be digits only (up to 20).");
        }

        if (sha is not null && sha.Length > 30)
        {
            e.Add(prefix + "shaNo", "Must be at most 30 characters.");
        }

        if (helb is not null && helb.Length > 30)
        {
            e.Add(prefix + "helbRef", "Must be at most 30 characters.");
        }

        if (disability is not null && disability.Length > 30)
        {
            e.Add(prefix + "disabilityCertNo", "Must be at most 30 characters.");
        }

        if ((disability is null) != (i.DisabilityCertExpiry is null))
        {
            e.Add(prefix + "disabilityCertExpiry", "A disability exemption needs both the certificate number and its expiry date.");
        }

        if (e.Items.Count > start)
        {
            return null;
        }

        return new Identifiers(nationalId, passport, permit, permit is null ? null : i.WorkPermitExpiry, kra, nssf, sha, helb, disability,
            disability is null ? null : i.DisabilityCertExpiry);
    }

    public static List<Contact>? NormalizeContacts(IReadOnlyList<EmergencyContactInput>? contacts, FieldErrors e, string prefix = "emergencyContacts")
    {
        if (contacts is null || contacts.Count == 0)
        {
            e.Add(prefix, "Add at least one emergency contact.");
            return null;
        }

        if (contacts.Count > 5)
        {
            e.Add(prefix, "Add at most 5 emergency contacts.");
            return null;
        }

        var start = e.Items.Count;
        var result = new List<Contact>();
        for (var n = 0; n < contacts.Count; n++)
        {
            var c = contacts[n];
            var p = $"{prefix}[{n}].";
            var name = Name(c.Name, p + "name", true, e, 120);
            var relationship = Clean(c.Relationship);
            if (relationship is null)
            {
                e.Add(p + "relationship", "Relationship is required.");
            }
            else if (relationship.Length > 60)
            {
                e.Add(p + "relationship", "Must be at most 60 characters.");
            }

            var phone = KenyanFormats.NormalizeMobile(c.Phone);
            var other = phone ?? (Clean(c.Phone) is { } raw && InternationalPhone().IsMatch(raw.Replace(" ", string.Empty)) ? raw.Replace(" ", string.Empty) : null);
            if (other is null)
            {
                e.Add(p + "phone", "Enter a phone number in international format, for example +254712345678.");
            }

            if (name is not null && relationship is not null && other is not null)
            {
                result.Add(new Contact(name, relationship, other));
            }
        }

        return e.Items.Count > start ? null : result;
    }

    public static Assignment? NormalizeAssignment(AssignmentInput? a, DateOnly effectiveFrom, FieldErrors e, string prefix = "assignment.")
    {
        if (a is null)
        {
            e.Add(prefix.TrimEnd('.'), "Assignment details are required.");
            return null;
        }

        var start = e.Items.Count;
        var category = Clean(a.WorkerCategory)?.ToLowerInvariant();
        var basis = Clean(a.PayBasis)?.ToLowerInvariant();
        if (category is null || !WorkerCategories.AllowedPayBases.TryGetValue(category, out var allowed))
        {
            e.Add(prefix + "workerCategory", "Choose permanent, fixed_term, probation, casual, piece_rate, intern or contractor.");
        }
        else if (basis is null || !allowed.Contains(basis))
        {
            e.Add(prefix + "payBasis", $"A {category.Replace('_', ' ')} worker is paid {string.Join(" or ", allowed.Order())}.");
        }

        var title = Clean(a.JobTitle);
        if (title is { Length: > 120 })
        {
            e.Add(prefix + "jobTitle", "Must be at most 120 characters.");
        }

        if (category == WorkerCategories.FixedTerm && a.ContractEndDate is null)
        {
            e.Add(prefix + "contractEndDate", "Fixed-term contracts need an end date.");
        }

        if (a.ContractEndDate is { } end && end < effectiveFrom)
        {
            e.Add(prefix + "contractEndDate", "The contract cannot end before this assignment starts.");
        }

        if (category == WorkerCategories.Probation && a.ProbationEndDate is null)
        {
            e.Add(prefix + "probationEndDate", "Probation needs an end date.");
        }

        if (a.ProbationEndDate is { } pe && pe <= effectiveFrom)
        {
            e.Add(prefix + "probationEndDate", "Probation must end after the assignment starts.");
        }

        if (e.Items.Count > start)
        {
            return null;
        }

        return new Assignment(category!, basis!, title, a.ContractEndDate, a.ProbationEndDate, a.DepartmentId, a.PositionId, a.BranchId,
            a.CostCenterId, a.GradeId, a.WorkScheduleId, a.ManagerEmployeeId);
    }

    public static PaymentDetails? NormalizePayment(PaymentDetailsInput? p, FieldErrors e, string prefix = "paymentDetails.")
    {
        if (p is null)
        {
            e.Add(prefix.TrimEnd('.'), "Payment details are required.");
            return null;
        }

        var start = e.Items.Count;
        var method = Clean(p.PaymentMethod)?.ToLowerInvariant();
        if (method is null || !PaymentMethods.All.Contains(method))
        {
            e.Add(prefix + "paymentMethod", "Choose bank, mpesa or cash.");
            return null;
        }

        string? bank = null, branch = null, account = null, accountName = null, mpesa = null;
        switch (method)
        {
            case PaymentMethods.Bank:
                bank = Clean(p.BankCode)?.ToUpperInvariant();
                branch = Clean(p.BankBranchCode)?.ToUpperInvariant();
                account = Clean(p.AccountNo)?.Replace(" ", string.Empty).ToUpperInvariant();
                accountName = Clean(p.AccountName);
                if (bank is null) e.Add(prefix + "bankCode", "Bank is required.");
                if (branch is null) e.Add(prefix + "bankBranchCode", "Bank branch is required.");
                if (account is null || !AccountNumber().IsMatch(account)) e.Add(prefix + "accountNo", "Enter the account number (letters and digits, up to 30).");
                if (accountName is null) e.Add(prefix + "accountName", "Account name is required.");
                else if (accountName.Length > 120) e.Add(prefix + "accountName", "Must be at most 120 characters.");
                break;
            case PaymentMethods.Mpesa:
                mpesa = KenyanFormats.NormalizeMobile(p.MpesaPhone);
                if (mpesa is null) e.Add(prefix + "mpesaPhone", "Enter the M-Pesa registered Kenyan mobile number.");
                break;
        }

        return e.Items.Count > start ? null : new PaymentDetails(method, bank, branch, account, accountName, mpesa);
    }

    /// <summary>Data PAY-05 will block payroll approval on (reported now, enforced in S3).</summary>
    public static List<string> Gaps(Employee e, string? currentCategory, DateOnly today)
    {
        var gaps = new List<string>();
        if (currentCategory is not null && WorkerCategories.IsStatutoryEmployee(currentCategory))
        {
            if (e.NssfNo is null) gaps.Add(StatutoryGaps.NssfMissing);
            if (e.ShaNo is null) gaps.Add(StatutoryGaps.ShaMissing);
        }

        if (e.PaymentMethod is null) gaps.Add(StatutoryGaps.PaymentNotApproved);
        if (e.Citizenship == Citizenships.NonKenyan && (e.WorkPermitExpiry is null || e.WorkPermitExpiry < today)) gaps.Add(StatutoryGaps.WorkPermitExpired);
        if (e.DisabilityCertExpiry is { } d && d < today) gaps.Add(StatutoryGaps.DisabilityCertificateExpired);
        return gaps;
    }

    /// <summary>Work-permit alert bucket (Section 15: alert 60, 30 and 7 days ahead).</summary>
    public static string? PermitAlert(DateOnly? expiry, DateOnly today)
    {
        if (expiry is not { } d)
        {
            return null;
        }

        var days = d.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => "expired",
            <= 7 => "7_days",
            <= 30 => "30_days",
            <= 60 => "60_days",
            _ => null,
        };
    }

    public static int AgeOn(DateOnly dob, DateOnly date)
    {
        var age = date.Year - dob.Year;
        if (date < dob.AddYears(age))
        {
            age--;
        }

        return age;
    }

    public static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : MultiSpace().Replace(v.Trim(), " ");

    private static string? Name(string? value, string field, bool required, FieldErrors e, int max = 80)
    {
        var v = Clean(value);
        if (v is null)
        {
            if (required)
            {
                e.Add(field, "This field is required.");
            }

            return null;
        }

        if (v.Length > max)
        {
            e.Add(field, $"Must be at most {max} characters.");
        }

        return v;
    }

    private static string? Code(string? value, IReadOnlySet<string> allowed, string field, string choices, FieldErrors e)
    {
        var v = Clean(value)?.ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        if (v is null || !allowed.Contains(v))
        {
            e.Add(field, $"Choose {choices}.");
            return null;
        }

        return v;
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9/_-]*$")]
    private static partial Regex EmployeeNumber();

    [GeneratedRegex("^[A-Z0-9-]{1,30}$")]
    private static partial Regex AccountNumber();

    [GeneratedRegex("^\\+\\d{8,15}$")]
    private static partial Regex InternationalPhone();

    [GeneratedRegex("\\s+")]
    private static partial Regex MultiSpace();
}
