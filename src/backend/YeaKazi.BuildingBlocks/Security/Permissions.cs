namespace YeaKazi.BuildingBlocks.Security;

/// <summary>
/// Permission catalogue. Endpoints check permissions, never role names, so tenants can
/// build custom roles from this catalogue without code changes.
/// Permissions marked "future" are defined now so that the Section 6 system roles are
/// seeded completely; the endpoints that use them arrive with later slices.
/// </summary>
public static class Permissions
{
    // S1
    public const string OrgRead = "org.read";
    public const string OrgWrite = "org.write";
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string AuditRead = "audit.read";

    // Future slices (S2+)
    public const string SelfRead = "self.read";
    public const string TeamRead = "team.read";
    public const string PeopleRead = "people.read";
    public const string PeopleWrite = "people.write";
    public const string PeopleSensitiveRead = "people.sensitive.read";
    public const string LeaveApprove = "leave.approve";
    public const string TimeApprove = "time.approve";
    public const string PayrollRead = "payroll.read";
    public const string PayrollPrepare = "payroll.prepare";
    public const string PayrollApprove = "payroll.approve";
    public const string PaymentsRelease = "payments.release";
    public const string AccountingRead = "accounting.read";

    public static readonly IReadOnlyDictionary<string, string> Catalogue = new Dictionary<string, string>
    {
        [OrgRead] = "View organization setup",
        [OrgWrite] = "Change organization setup",
        [UsersRead] = "View users and their roles",
        [UsersManage] = "Invite, disable and assign roles to users",
        [RolesManage] = "Create and change custom roles",
        [AuditRead] = "Search the audit log",
        [SelfRead] = "View own records, requests and published payslips",
        [TeamRead] = "View authorized team records",
        [PeopleRead] = "View employee records",
        [PeopleWrite] = "Administer employee records and lifecycle",
        [PeopleSensitiveRead] = "View restricted employee fields",
        [LeaveApprove] = "Approve team leave",
        [TimeApprove] = "Approve team attendance",
        [PayrollRead] = "View payroll runs and totals",
        [PayrollPrepare] = "Prepare payroll inputs, calculations and draft outputs",
        [PayrollApprove] = "Review, approve and lock payroll",
        [PaymentsRelease] = "Release payment batches",
        [AccountingRead] = "View approved totals and accounting queues",
    };

    /// <summary>Permissions that let a holder change data or approve; holders must use MFA.</summary>
    public static readonly IReadOnlySet<string> Privileged = new HashSet<string>
    {
        OrgWrite, UsersManage, RolesManage, AuditRead, PeopleWrite, PeopleSensitiveRead,
        PayrollRead, PayrollPrepare, PayrollApprove, PaymentsRelease, AccountingRead,
    };
}

/// <summary>Seeded system roles per tenant (Section 6). System roles cannot be edited by tenants.</summary>
public static class SystemRoles
{
    public const string Employee = "employee";
    public const string LineManager = "line_manager";
    public const string HrOfficer = "hr_officer";
    public const string PayrollPreparer = "payroll_preparer";
    public const string PayrollApprover = "payroll_approver";
    public const string FinanceOfficer = "finance_officer";
    public const string TenantAdministrator = "tenant_administrator";
    public const string Auditor = "auditor";

    public sealed record Definition(string Code, string Name, string Description, IReadOnlyList<string> Permissions);

    public static readonly IReadOnlyList<Definition> All =
    [
        new(Employee, "Employee", "Own records, requests, published slips and policies",
            [Security.Permissions.SelfRead]),
        new(LineManager, "Line manager", "Authorized team, leave and time approvals; no salary access unless granted",
            [Security.Permissions.SelfRead, Security.Permissions.OrgRead, Security.Permissions.TeamRead,
             Security.Permissions.LeaveApprove, Security.Permissions.TimeApprove]),
        new(HrOfficer, "HR officer", "Employee administration and lifecycle tasks; no payroll approval",
            [Security.Permissions.SelfRead, Security.Permissions.OrgRead, Security.Permissions.PeopleRead,
             Security.Permissions.PeopleWrite, Security.Permissions.PeopleSensitiveRead]),
        new(PayrollPreparer, "Payroll preparer", "Inputs, calculations and draft outputs; cannot approve own run",
            [Security.Permissions.SelfRead, Security.Permissions.OrgRead, Security.Permissions.PeopleRead,
             Security.Permissions.PayrollRead, Security.Permissions.PayrollPrepare]),
        new(PayrollApprover, "Payroll approver", "Review, approve and lock payroll; distinct from preparer",
            [Security.Permissions.SelfRead, Security.Permissions.OrgRead, Security.Permissions.PayrollRead,
             Security.Permissions.PayrollApprove]),
        new(FinanceOfficer, "Finance officer", "Approved totals, payment and accounting queues; no HR case notes",
            [Security.Permissions.SelfRead, Security.Permissions.OrgRead, Security.Permissions.AccountingRead,
             Security.Permissions.PaymentsRelease]),
        new(TenantAdministrator, "Tenant administrator", "Users, roles and configuration; payroll content access granted separately",
            [Security.Permissions.SelfRead, Security.Permissions.OrgRead, Security.Permissions.OrgWrite,
             Security.Permissions.UsersRead, Security.Permissions.UsersManage, Security.Permissions.RolesManage,
             Security.Permissions.AuditRead]),
        new(Auditor, "Auditor", "Authorized read-only records and audit exports; cannot amend or approve",
            [Security.Permissions.SelfRead, Security.Permissions.OrgRead, Security.Permissions.UsersRead,
             Security.Permissions.AuditRead, Security.Permissions.PeopleRead, Security.Permissions.PayrollRead,
             Security.Permissions.AccountingRead]),
    ];
}
