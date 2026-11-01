namespace YeaKazi.Platform.Auth;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Issuer { get; set; } = "yeakazi";
    public string Audience { get; set; } = "yeakazi-api";

    /// <summary>Base64 HMAC-SHA256 key, at least 32 bytes. Supplied by secret store in non-dev environments.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 30;
    public int ChallengeTokenMinutes { get; set; } = 5;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int InvitationHours { get; set; } = 72;
    public string MfaIssuer { get; set; } = "YeaKazi";
    public string PortalBaseUrl { get; set; } = "http://localhost:5173";
}

public static class AuthClaims
{
    public const string Scope = "scope";
    public const string TenantId = "tid";
    public const string Amr = "amr";
    public const string Purpose = "purpose";
    public const string SecurityStamp = "sst";
    public const string PlatformRole = "prole";

    public const string ScopeTenant = "tenant";
    public const string ScopePlatform = "platform";

    public const string AmrPassword = "pwd";
    public const string AmrMfa = "mfa";

    public const string PurposeMfaVerify = "mfa_verify";
    public const string PurposeMfaEnroll = "mfa_enroll";

    public const string ChallengeAudience = "yeakazi-mfa";
}
