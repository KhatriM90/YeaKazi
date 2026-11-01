using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Platform.Access;
using YeaKazi.Platform.Domain;
using YeaKazi.Platform.Persistence;

namespace YeaKazi.Platform.Auth;

public sealed record LoginRequest(string Tenant, string Email, string Password);

public sealed record MfaVerifyRequest(string ChallengeToken, string Code);

public sealed record MfaEnrollRequest(string ChallengeToken);

public sealed record ActivateRequest(string Tenant, string Token, string Password);

public static class LoginStatuses
{
    public const string Authenticated = "authenticated";
    public const string MfaRequired = "mfa_required";
    public const string MfaEnrollmentRequired = "mfa_enrollment_required";
}

/// <summary>RecoveryCodes is set only once, when MFA enrolment completes; the codes are never shown again.</summary>
public sealed record LoginResponse(string Status, string? AccessToken, DateTimeOffset? ExpiresAt, string? ChallengeToken, IReadOnlyList<string>? RecoveryCodes = null);

public sealed record MfaEnrollResponse(string Secret, string OtpAuthUri);

/// <summary>Tenant user sign-in: password, then TOTP MFA when enrolled or required by role.</summary>
public sealed class TenantAuthService(
    PlatformDbContext db,
    RequestContext context,
    TokenService tokens,
    IFieldEncryptor encryptor,
    AuditTrail audit,
    IOptions<AuthOptions> options,
    MfaRecoveryService recovery,
    TimeProvider clock)
{
    private const string InvalidCredentials = "Email, password or organization is incorrect.";
    private readonly AuthOptions _options = options.Value;

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        new Validator()
            .Required("tenant", request.Tenant, 40)
            .Required("email", request.Email, 254)
            .Required("password", request.Password, 256)
            .ThrowIfInvalid();

        var tenantId = await ResolveTenantAsync(request.Tenant, ct);
        if (tenantId is null)
        {
            PasswordPolicy.BurnTime(request.Password);
            throw new UnauthorizedException("invalid_credentials", InvalidCredentials);
        }

        context.SetTenant(tenantId.Value);
        var normalized = Normalize(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        var now = clock.GetUtcNow();

        if (user is null)
        {
            PasswordPolicy.BurnTime(request.Password);
            context.SetSystemActor("anonymous");
            audit.Record(db, "auth.login_failed", "user", null,
                after: AuditSnapshot.Of(new { email = AuditSnapshot.MaskEmail(request.Email), reason = "unknown_account" }));
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException("invalid_credentials", InvalidCredentials);
        }

        context.SetTenantUser(tenantId.Value, user.Id, user.DisplayName);

        if (user.Status != UserStatuses.Active || (user.LockedUntil is { } until && until > now))
        {
            PasswordPolicy.BurnTime(request.Password);
            audit.Record(db, "auth.login_failed", "user", user.Id,
                after: AuditSnapshot.Of(new { reason = user.Status != UserStatuses.Active ? "inactive_account" : "locked" }));
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException("invalid_credentials", InvalidCredentials);
        }

        if (!PasswordPolicy.Verify(user.PasswordHash, request.Password))
        {
            await RegisterFailureAsync(user, "wrong_password", ct);
            throw new UnauthorizedException("invalid_credentials", InvalidCredentials);
        }

        user.FailedAttempts = 0;
        user.LockedUntil = null;

        var permissions = await AccessControl.LoadForUserAsync(db, user.Id, ct);
        var mfaRequired = AccessControl.RequiresMfa(permissions.Keys);

        if (user.MfaEnabled)
        {
            audit.Record(db, "auth.password_verified", "user", user.Id);
            await db.SaveChangesAsync(ct);
            var challenge = tokens.IssueChallenge(AuthClaims.ScopeTenant, user.Id, tenantId, AuthClaims.PurposeMfaVerify, user.SecurityStamp);
            return new LoginResponse(LoginStatuses.MfaRequired, null, null, challenge.Token);
        }

        if (mfaRequired)
        {
            audit.Record(db, "auth.password_verified", "user", user.Id,
                after: AuditSnapshot.Of(new { mfa = "enrollment_required" }));
            await db.SaveChangesAsync(ct);
            var challenge = tokens.IssueChallenge(AuthClaims.ScopeTenant, user.Id, tenantId, AuthClaims.PurposeMfaEnroll, user.SecurityStamp);
            return new LoginResponse(LoginStatuses.MfaEnrollmentRequired, null, null, challenge.Token);
        }

        user.LastLoginAt = now;
        audit.Record(db, "auth.login_succeeded", "user", user.Id, after: AuditSnapshot.Of(new { amr = AuthClaims.AmrPassword }));
        await db.SaveChangesAsync(ct);
        var access = tokens.IssueAccessToken(AuthClaims.ScopeTenant, user.Id, tenantId, user.DisplayName, AuthClaims.AmrPassword, user.SecurityStamp);
        return new LoginResponse(LoginStatuses.Authenticated, access.Token, access.ExpiresAt, null);
    }

    public async Task<MfaEnrollResponse> BeginEnrollmentAsync(MfaEnrollRequest request, CancellationToken ct)
    {
        var (user, _) = await LoadChallengeUserAsync(request.ChallengeToken, AuthClaims.PurposeMfaEnroll, ct);
        if (user.MfaEnabled)
        {
            throw new ConflictException("mfa_already_enrolled", "Multi-factor sign-in is already set up.");
        }

        var secret = Totp.NewSecret();
        user.MfaPendingSecret = encryptor.Encrypt(Base32.Encode(secret));
        audit.Record(db, "auth.mfa_enrollment_started", "user", user.Id);
        await db.SaveChangesAsync(ct);
        return new MfaEnrollResponse(Base32.Encode(secret), Totp.OtpAuthUri(_options.MfaIssuer, user.Email, secret));
    }

    public async Task<LoginResponse> VerifyMfaAsync(MfaVerifyRequest request, CancellationToken ct)
    {
        var claims = tokens.ReadChallenge(request.ChallengeToken, AuthClaims.ScopeTenant);
        var (user, tenantId) = await LoadChallengeUserAsync(request.ChallengeToken, claims.Purpose, ct);
        var enrolling = claims.Purpose == AuthClaims.PurposeMfaEnroll;
        var encrypted = enrolling ? user.MfaPendingSecret : user.MfaSecret;
        if (encrypted is null)
        {
            throw new UnauthorizedException("invalid_challenge", "Start multi-factor setup first.");
        }

        var secret = Base32.Decode(encryptor.Decrypt(encrypted));
        var now = clock.GetUtcNow();
        var step = Totp.Verify(secret, request.Code?.Trim(), now, user.MfaLastStep);
        if (step is null)
        {
            await RegisterFailureAsync(user, "wrong_mfa_code", ct);
            throw new UnauthorizedException("invalid_mfa_code", "The verification code is incorrect or has already been used.");
        }

        user.MfaLastStep = step.Value;
        user.FailedAttempts = 0;
        user.LastLoginAt = now;
        IReadOnlyList<string>? codes = null;
        if (enrolling)
        {
            user.MfaSecret = user.MfaPendingSecret;
            user.MfaPendingSecret = null;
            user.MfaEnabled = true;
            audit.Record(db, "auth.mfa_enrolled", "user", user.Id);
            codes = await recovery.IssueAsync(user, ct);
        }

        audit.Record(db, "auth.login_succeeded", "user", user.Id, after: AuditSnapshot.Of(new { amr = AuthClaims.AmrMfa }));
        await db.SaveChangesAsync(ct);
        var access = tokens.IssueAccessToken(AuthClaims.ScopeTenant, user.Id, tenantId, user.DisplayName, AuthClaims.AmrMfa, user.SecurityStamp);
        return new LoginResponse(LoginStatuses.Authenticated, access.Token, access.ExpiresAt, null, codes);
    }

    /// <summary>
    /// Second factor by single-use recovery code, for a user who has lost their authenticator.
    /// Counts towards lockout like a wrong TOTP code; success is audited and emailed to the user.
    /// </summary>
    public async Task<LoginResponse> RecoverWithCodeAsync(MfaRecoverRequest request, CancellationToken ct)
    {
        var (user, tenantId) = await LoadChallengeUserAsync(request.ChallengeToken, AuthClaims.PurposeMfaVerify, ct);
        if (!user.MfaEnabled)
        {
            throw new UnauthorizedException("invalid_challenge", "The sign-in challenge is missing or has expired.");
        }

        var left = await recovery.TryConsumeAsync(user, request.RecoveryCode, ct);
        if (left is null)
        {
            await RegisterFailureAsync(user, "wrong_recovery_code", ct);
            throw new UnauthorizedException("invalid_recovery_code", "The recovery code is incorrect or has already been used.");
        }

        user.FailedAttempts = 0;
        user.LastLoginAt = clock.GetUtcNow();
        audit.Record(db, "auth.mfa_recovery_code_used", "user", user.Id, after: AuditSnapshot.Of(new { remaining = left.Value }));
        audit.Record(db, "auth.login_succeeded", "user", user.Id, after: AuditSnapshot.Of(new { amr = AuthClaims.AmrMfa, method = "recovery_code" }));
        await recovery.NotifyAsync(user, SecurityNoticeKinds.RecoveryCodeUsed, ct);
        await db.SaveChangesAsync(ct);
        var access = tokens.IssueAccessToken(AuthClaims.ScopeTenant, user.Id, tenantId, user.DisplayName, AuthClaims.AmrMfa, user.SecurityStamp);
        return new LoginResponse(LoginStatuses.Authenticated, access.Token, access.ExpiresAt, null);
    }

    /// <summary>Replaces the signed-in user's recovery codes after a fresh TOTP check (step-up).</summary>
    public async Task<RecoveryCodesResponse> RegenerateRecoveryCodesAsync(RegenerateRecoveryCodesRequest request, CancellationToken ct)
    {
        var userId = context.ActorUserId ?? throw new ForbiddenException();
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (!user.MfaEnabled || user.MfaSecret is null)
        {
            throw new BusinessRuleException("mfa_not_enrolled", "Set up multi-factor sign-in first.");
        }

        var step = Totp.Verify(Base32.Decode(encryptor.Decrypt(user.MfaSecret)), request.Code?.Trim(), clock.GetUtcNow(), user.MfaLastStep);
        if (step is null)
        {
            audit.Record(db, "auth.step_up_failed", "user", user.Id, after: AuditSnapshot.Of(new { purpose = "regenerate_recovery_codes" }));
            await db.SaveChangesAsync(ct);
            throw new BusinessRuleException("invalid_mfa_code", "The verification code is incorrect or has already been used.");
        }

        user.MfaLastStep = step.Value;
        var codes = await recovery.IssueAsync(user, ct);
        await recovery.NotifyAsync(user, SecurityNoticeKinds.RecoveryCodesRegenerated, ct);
        await db.SaveChangesAsync(ct);
        return new RecoveryCodesResponse(codes);
    }

    public async Task ActivateAsync(ActivateRequest request, CancellationToken ct)
    {
        var v = new Validator()
            .Required("tenant", request.Tenant, 40)
            .Required("token", request.Token, 200);
        foreach (var error in PasswordPolicy.Validate(request.Password))
        {
            v.Add("password", error);
        }

        v.ThrowIfInvalid();

        var tenantId = await ResolveTenantAsync(request.Tenant, ct)
            ?? throw new BusinessRuleException("invalid_invitation", "The invitation is invalid or has expired.");
        context.SetTenant(tenantId);

        var hash = HashToken(request.Token);
        var invitation = await db.UserInvitations.SingleOrDefaultAsync(i => i.TokenHash == hash, ct);
        var now = clock.GetUtcNow();
        if (invitation is null || invitation.UsedAt is not null || invitation.ExpiresAt <= now)
        {
            throw new BusinessRuleException("invalid_invitation", "The invitation is invalid or has expired.");
        }

        var user = await db.Users.SingleAsync(u => u.Id == invitation.UserId, ct);
        if (user.Status != UserStatuses.Invited)
        {
            throw new BusinessRuleException("invalid_invitation", "The invitation is invalid or has expired.");
        }

        context.SetTenantUser(tenantId, user.Id, user.DisplayName);
        var before = AuditSnapshot.Of(user);
        user.PasswordHash = PasswordPolicy.Hash(request.Password);
        user.Status = UserStatuses.Active;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        invitation.UsedAt = now;
        audit.Record(db, "user.activated", "user", user.Id, before, AuditSnapshot.Of(user));
        await db.SaveChangesAsync(ct);
    }

    internal static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    internal static string Normalize(string email) => email.Trim().ToUpperInvariant();

    private async Task<(User User, Guid TenantId)> LoadChallengeUserAsync(string token, string purpose, CancellationToken ct)
    {
        var claims = tokens.ReadChallenge(token, AuthClaims.ScopeTenant);
        if (claims.Purpose != purpose || claims.TenantId is null)
        {
            throw new UnauthorizedException("invalid_challenge", "The sign-in challenge is missing or has expired.");
        }

        context.SetTenant(claims.TenantId.Value);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == claims.SubjectId, ct);
        var now = clock.GetUtcNow();
        if (user is null || user.Status != UserStatuses.Active || user.SecurityStamp != claims.SecurityStamp
            || (user.LockedUntil is { } until && until > now))
        {
            throw new UnauthorizedException("invalid_challenge", "The sign-in challenge is missing or has expired.");
        }

        context.SetTenantUser(claims.TenantId.Value, user.Id, user.DisplayName);
        return (user, claims.TenantId.Value);
    }

    private async Task RegisterFailureAsync(User user, string reason, CancellationToken ct)
    {
        user.FailedAttempts++;
        var locked = false;
        if (user.FailedAttempts >= _options.MaxFailedAttempts)
        {
            user.LockedUntil = clock.GetUtcNow().AddMinutes(_options.LockoutMinutes);
            user.FailedAttempts = 0;
            locked = true;
        }

        audit.Record(db, "auth.login_failed", "user", user.Id, after: AuditSnapshot.Of(new { reason, locked }));
        if (locked)
        {
            audit.Record(db, "auth.account_locked", "user", user.Id);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Resolves an active tenant by slug through a SECURITY DEFINER function (no tenant data exposed).</summary>
    private async Task<Guid?> ResolveTenantAsync(string slug, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var ids = await db.Database
            .SqlQuery<Guid>($"select tenant_id as \"Value\" from platform.resolve_active_tenant({normalized})")
            .ToListAsync(ct);
        return ids.Count == 1 ? ids[0] : null;
    }
}
