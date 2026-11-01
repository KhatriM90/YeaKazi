using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using YeaKazi.BuildingBlocks.Errors;

namespace YeaKazi.Platform.Auth;

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt);

public sealed record ChallengeClaims(string Scope, Guid? TenantId, Guid SubjectId, string Purpose, string SecurityStamp);

/// <summary>Issues signed JWT access tokens and short-lived MFA challenge tokens.</summary>
public sealed class TokenService
{
    private readonly AuthOptions _options;
    private readonly SymmetricSecurityKey _key;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public TokenService(IOptions<AuthOptions> options)
    {
        _options = options.Value;
        _key = CreateKey(_options);
    }

    public static SymmetricSecurityKey CreateKey(AuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            throw new InvalidOperationException("Auth:SigningKey is not configured.");
        }

        var bytes = Convert.FromBase64String(options.SigningKey);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be at least 32 bytes.");
        }

        return new SymmetricSecurityKey(bytes) { KeyId = "k1" };
    }

    public IssuedToken IssueAccessToken(string scope, Guid subjectId, Guid? tenantId, string displayName, string amr, string securityStamp, string? platformRole = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subjectId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Name, displayName),
            new(AuthClaims.Scope, scope),
            new(AuthClaims.Amr, amr),
            new(AuthClaims.SecurityStamp, securityStamp),
        };
        if (tenantId is not null)
        {
            claims.Add(new Claim(AuthClaims.TenantId, tenantId.Value.ToString()));
        }

        if (platformRole is not null)
        {
            claims.Add(new Claim(AuthClaims.PlatformRole, platformRole));
        }

        return Write(claims, _options.Audience, TimeSpan.FromMinutes(_options.AccessTokenMinutes));
    }

    public IssuedToken IssueChallenge(string scope, Guid subjectId, Guid? tenantId, string purpose, string securityStamp)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subjectId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(AuthClaims.Scope, scope),
            new(AuthClaims.Purpose, purpose),
            new(AuthClaims.SecurityStamp, securityStamp),
        };
        if (tenantId is not null)
        {
            claims.Add(new Claim(AuthClaims.TenantId, tenantId.Value.ToString()));
        }

        return Write(claims, AuthClaims.ChallengeAudience, TimeSpan.FromMinutes(_options.ChallengeTokenMinutes));
    }

    public ChallengeClaims ReadChallenge(string? token, string expectedScope)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new UnauthorizedException("invalid_challenge", "The sign-in challenge is missing or has expired.");
        }

        try
        {
            var principal = _handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = _options.Issuer,
                ValidAudience = AuthClaims.ChallengeAudience,
                IssuerSigningKey = _key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            }, out _);

            var scope = principal.FindFirstValue(AuthClaims.Scope);
            if (scope != expectedScope)
            {
                throw new SecurityTokenException("Wrong scope.");
            }

            var tid = principal.FindFirstValue(AuthClaims.TenantId);
            return new ChallengeClaims(
                scope,
                tid is null ? null : Guid.Parse(tid),
                Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!),
                principal.FindFirstValue(AuthClaims.Purpose)!,
                principal.FindFirstValue(AuthClaims.SecurityStamp)!);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or FormatException)
        {
            throw new UnauthorizedException("invalid_challenge", "The sign-in challenge is missing or has expired.");
        }
    }

    public TokenValidationParameters AccessTokenValidation() => new()
    {
        ValidIssuer = _options.Issuer,
        ValidAudience = _options.Audience,
        IssuerSigningKey = _key,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtRegisteredClaimNames.Name,
    };

    private IssuedToken Write(IEnumerable<Claim> claims, string audience, TimeSpan lifetime)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(lifetime);
        var token = new JwtSecurityToken(
            _options.Issuer,
            audience,
            claims,
            now.UtcDateTime,
            expires.UtcDateTime,
            new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return new IssuedToken(_handler.WriteToken(token), expires);
    }
}
