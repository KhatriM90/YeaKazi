using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Errors;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Platform.Domain;
using YeaKazi.Platform.Persistence;

namespace YeaKazi.Platform.Auth;

public sealed record PlatformLoginRequest(string Email, string Password);

/// <summary>Platform operator sign-in. MFA is always required for platform staff.</summary>
public sealed class PlatformAuthService(
    PlatformDbContext db,
    RequestContext context,
    TokenService tokens,
    IFieldEncryptor encryptor,
    AuditTrail audit,
    IOptions<AuthOptions> options,
    TimeProvider clock)
{
    private const string InvalidCredentials = "Email or password is incorrect.";
    private readonly AuthOptions _options = options.Value;

    public async Task<LoginResponse> LoginAsync(PlatformLoginRequest request, CancellationToken ct)
    {
        new Validator().Required("email", request.Email, 254).Required("password", request.Password, 256).ThrowIfInvalid();
        context.EnterPlatformScope();
        var normalized = TenantAuthService.Normalize(request.Email);
        var user = await db.PlatformUsers.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        var now = clock.GetUtcNow();

        if (user is null || !user.IsActive || (user.LockedUntil is { } until && until > now))
        {
            PasswordPolicy.BurnTime(request.Password);
            context.SetSystemActor("anonymous");
            audit.RecordPlatform(db, "platform.auth.login_failed", "platform_user", user?.Id, null,
                AuditSnapshot.Of(new { email = AuditSnapshot.MaskEmail(request.Email) }));
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException("invalid_credentials", InvalidCredentials);
        }

        context.SetPlatformUser(user.Id, user.DisplayName);
        if (!PasswordPolicy.Verify(user.PasswordHash, request.Password))
        {
            await RegisterFailureAsync(user, ct);
            throw new UnauthorizedException("invalid_credentials", InvalidCredentials);
        }

        user.FailedAttempts = 0;
        audit.RecordPlatform(db, "platform.auth.password_verified", "platform_user", user.Id, null);
        await db.SaveChangesAsync(ct);
        var purpose = user.MfaEnabled ? AuthClaims.PurposeMfaVerify : AuthClaims.PurposeMfaEnroll;
        var challenge = tokens.IssueChallenge(AuthClaims.ScopePlatform, user.Id, null, purpose, user.SecurityStamp);
        return new LoginResponse(
            user.MfaEnabled ? LoginStatuses.MfaRequired : LoginStatuses.MfaEnrollmentRequired,
            null, null, challenge.Token);
    }

    public async Task<MfaEnrollResponse> BeginEnrollmentAsync(MfaEnrollRequest request, CancellationToken ct)
    {
        var user = await LoadChallengeUserAsync(request.ChallengeToken, AuthClaims.PurposeMfaEnroll, ct);
        if (user.MfaEnabled)
        {
            throw new ConflictException("mfa_already_enrolled", "Multi-factor sign-in is already set up.");
        }

        var secret = Totp.NewSecret();
        user.MfaPendingSecret = encryptor.Encrypt(Base32.Encode(secret));
        audit.RecordPlatform(db, "platform.auth.mfa_enrollment_started", "platform_user", user.Id, null);
        await db.SaveChangesAsync(ct);
        return new MfaEnrollResponse(Base32.Encode(secret), Totp.OtpAuthUri(_options.MfaIssuer + " Platform", user.Email, secret));
    }

    public async Task<LoginResponse> VerifyMfaAsync(MfaVerifyRequest request, CancellationToken ct)
    {
        var claims = tokens.ReadChallenge(request.ChallengeToken, AuthClaims.ScopePlatform);
        var user = await LoadChallengeUserAsync(request.ChallengeToken, claims.Purpose, ct);
        var enrolling = claims.Purpose == AuthClaims.PurposeMfaEnroll;
        var encrypted = enrolling ? user.MfaPendingSecret : user.MfaSecret;
        if (encrypted is null)
        {
            throw new UnauthorizedException("invalid_challenge", "Start multi-factor setup first.");
        }

        var now = clock.GetUtcNow();
        var step = Totp.Verify(Base32.Decode(encryptor.Decrypt(encrypted)), request.Code?.Trim(), now, user.MfaLastStep);
        if (step is null)
        {
            await RegisterFailureAsync(user, ct);
            throw new UnauthorizedException("invalid_mfa_code", "The verification code is incorrect or has already been used.");
        }

        user.MfaLastStep = step.Value;
        user.FailedAttempts = 0;
        user.LastLoginAt = now;
        if (enrolling)
        {
            user.MfaSecret = user.MfaPendingSecret;
            user.MfaPendingSecret = null;
            user.MfaEnabled = true;
            audit.RecordPlatform(db, "platform.auth.mfa_enrolled", "platform_user", user.Id, null);
        }

        audit.RecordPlatform(db, "platform.auth.login_succeeded", "platform_user", user.Id, null);
        await db.SaveChangesAsync(ct);
        var access = tokens.IssueAccessToken(AuthClaims.ScopePlatform, user.Id, null, user.DisplayName, AuthClaims.AmrMfa, user.SecurityStamp, user.Role);
        return new LoginResponse(LoginStatuses.Authenticated, access.Token, access.ExpiresAt, null);
    }

    /// <summary>Creates a platform operator from the command line (bootstrap). Audited as a system action.</summary>
    public async Task<Guid> CreatePlatformUserAsync(string email, string displayName, string role, string password, CancellationToken ct)
    {
        var v = new Validator().Required("email", email, 254).Required("displayName", displayName, 200)
            .Must(role is PlatformRoles.Admin or PlatformRoles.Support, "role", "Unknown platform role.");
        foreach (var error in PasswordPolicy.Validate(password))
        {
            v.Add("password", error);
        }

        v.ThrowIfInvalid();
        context.EnterPlatformScope();
        context.SetSystemActor("cli");
        var user = new PlatformUser
        {
            Email = email.Trim(),
            NormalizedEmail = TenantAuthService.Normalize(email),
            DisplayName = displayName.Trim(),
            Role = role,
            PasswordHash = PasswordPolicy.Hash(password),
        };
        db.PlatformUsers.Add(user);
        audit.RecordPlatform(db, "platform.user.created", "platform_user", user.Id, null,
            AuditSnapshot.Of(new { email = AuditSnapshot.MaskEmail(user.Email), user.DisplayName, user.Role }));
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    private async Task<PlatformUser> LoadChallengeUserAsync(string token, string purpose, CancellationToken ct)
    {
        var claims = tokens.ReadChallenge(token, AuthClaims.ScopePlatform);
        if (claims.Purpose != purpose)
        {
            throw new UnauthorizedException("invalid_challenge", "The sign-in challenge is missing or has expired.");
        }

        context.EnterPlatformScope();
        var user = await db.PlatformUsers.SingleOrDefaultAsync(u => u.Id == claims.SubjectId, ct);
        var now = clock.GetUtcNow();
        if (user is null || !user.IsActive || user.SecurityStamp != claims.SecurityStamp
            || (user.LockedUntil is { } until && until > now))
        {
            throw new UnauthorizedException("invalid_challenge", "The sign-in challenge is missing or has expired.");
        }

        context.SetPlatformUser(user.Id, user.DisplayName);
        return user;
    }

    private async Task RegisterFailureAsync(PlatformUser user, CancellationToken ct)
    {
        user.FailedAttempts++;
        var locked = false;
        if (user.FailedAttempts >= _options.MaxFailedAttempts)
        {
            user.LockedUntil = clock.GetUtcNow().AddMinutes(_options.LockoutMinutes);
            user.FailedAttempts = 0;
            locked = true;
        }

        audit.RecordPlatform(db, "platform.auth.login_failed", "platform_user", user.Id, null, AuditSnapshot.Of(new { locked }));
        await db.SaveChangesAsync(ct);
    }
}
