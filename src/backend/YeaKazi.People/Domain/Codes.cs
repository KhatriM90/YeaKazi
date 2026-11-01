namespace YeaKazi.People.Domain;

/// <summary>EMP-04 worker categories and the pay bases each may use (docs/assumptions.md A-23).</summary>
public static class WorkerCategories
{
    public const string Permanent = "permanent";
    public const string FixedTerm = "fixed_term";
    public const string Probation = "probation";
    public const string Casual = "casual";
    public const string PieceRate = "piece_rate";
    public const string Intern = "intern";
    public const string Contractor = "contractor";

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedPayBases = new Dictionary<string, IReadOnlySet<string>>
    {
        [Permanent] = new HashSet<string> { PayBases.Monthly, PayBases.Daily, PayBases.Hourly },
        [FixedTerm] = new HashSet<string> { PayBases.Monthly, PayBases.Daily, PayBases.Hourly },
        [Probation] = new HashSet<string> { PayBases.Monthly, PayBases.Daily, PayBases.Hourly },
        [Casual] = new HashSet<string> { PayBases.Daily, PayBases.Hourly },
        [PieceRate] = new HashSet<string> { PayBases.PieceRate },
        [Intern] = new HashSet<string> { PayBases.Monthly, PayBases.Daily, PayBases.Hourly },
        [Contractor] = new HashSet<string> { PayBases.Monthly, PayBases.Daily, PayBases.Hourly, PayBases.PieceRate },
    };

    public static IEnumerable<string> All => AllowedPayBases.Keys;

    /// <summary>Contractors are paid through the contractor run and never appear in PAYE, NSSF, SHIF or AHL outputs (KEN-11).</summary>
    public static bool IsStatutoryEmployee(string category) => category != Contractor;
}

public static class PayBases
{
    public const string Monthly = "monthly";
    public const string Daily = "daily";
    public const string Hourly = "hourly";
    public const string PieceRate = "piece_rate";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Monthly, Daily, Hourly, PieceRate };
}

public static class Genders
{
    public static readonly IReadOnlySet<string> All = new HashSet<string> { "female", "male", "intersex" };
}

public static class Citizenships
{
    public const string Kenyan = "kenyan";
    public const string NonKenyan = "non_kenyan";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Kenyan, NonKenyan };
}

public static class ResidencyStatuses
{
    public static readonly IReadOnlySet<string> All = new HashSet<string> { "resident", "non_resident" };
}

public static class PaymentMethods
{
    public const string Bank = "bank";
    public const string Mpesa = "mpesa";
    public const string Cash = "cash";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Bank, Mpesa, Cash };
}

/// <summary>Kinds of assignment version (EMP-02).</summary>
public static class ChangeTypes
{
    public const string Hire = "hire";
    public const string Rehire = "rehire";
    public const string Promotion = "promotion";
    public const string Transfer = "transfer";
    public const string Change = "change";
    public const string Suspension = "suspension";
    public const string Reinstatement = "reinstatement";

    /// <summary>Types a user may add to an existing episode (hire and rehire start episodes).</summary>
    public static readonly IReadOnlySet<string> Manual = new HashSet<string> { Promotion, Transfer, Change, Suspension, Reinstatement };
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Hire, Rehire, Promotion, Transfer, Change, Suspension, Reinstatement };
}

public static class AssignmentStatuses
{
    public const string Active = "active";
    public const string Suspended = "suspended";
}

public static class TerminationTypes
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        "resignation", "end_of_contract", "dismissal", "redundancy", "retirement", "death", "mutual_agreement", "other",
    };
}

public static class EmployeeStatuses
{
    public const string PendingStart = "pending_start";
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Terminated = "terminated";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { PendingStart, Active, Suspended, Terminated };
}

public static class PaymentChangeStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Withdrawn = "withdrawn";
}

public static class ImportStatuses
{
    public const string Validated = "validated";
    public const string Committed = "committed";
    public const string Discarded = "discarded";
}

/// <summary>Data PAY-05 will need before payroll approval; reported on the employee now.</summary>
public static class StatutoryGaps
{
    public const string NssfMissing = "nssf_no_missing";
    public const string ShaMissing = "sha_no_missing";
    public const string PaymentNotApproved = "payment_details_not_approved";
    public const string WorkPermitExpired = "work_permit_expired";
    public const string DisabilityCertificateExpired = "disability_certificate_expired";
}
