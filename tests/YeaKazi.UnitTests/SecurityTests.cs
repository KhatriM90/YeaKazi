using System.Security.Cryptography;
using System.Text;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.Platform.Auth;

namespace YeaKazi.UnitTests;

public class TotpTests
{
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    // RFC 6238 Appendix B, SHA-1 test vectors (8 digits).
    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Matches_rfc6238_vectors(long unixTime, string expected)
    {
        var step = unixTime / Totp.StepSeconds;
        Assert.Equal(expected, Totp.Compute(RfcSecret, step, 8));
    }

    [Fact]
    public void Accepts_current_code_and_rejects_replay()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var code = Totp.Compute(secret, Totp.StepAt(now));
        var step = Totp.Verify(secret, code, now, 0);
        Assert.NotNull(step);
        Assert.Null(Totp.Verify(secret, code, now, step!.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    [InlineData("1234567")]
    public void Rejects_malformed_codes(string? code)
        => Assert.Null(Totp.Verify(Totp.NewSecret(), code, DateTimeOffset.UtcNow, 0));

    [Fact]
    public void Rejects_codes_outside_drift_window()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var old = Totp.Compute(secret, Totp.StepAt(now) - 3);
        Assert.Null(Totp.Verify(secret, old, now, 0));
    }

    [Fact]
    public void Base32_round_trips()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        Assert.Equal(bytes, Base32.Decode(Base32.Encode(bytes)));
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Base32.Encode(RfcSecret));
    }
}

public class FieldEncryptorTests
{
    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Round_trips_and_uses_fresh_nonce()
    {
        var enc = new AesGcmFieldEncryptor("k1", new Dictionary<string, string> { ["k1"] = Key() });
        var a = enc.Encrypt("secret");
        var b = enc.Encrypt("secret");
        Assert.NotEqual(a, b);
        Assert.StartsWith("k1:", a);
        Assert.Equal("secret", enc.Decrypt(a));
    }

    [Fact]
    public void Detects_tampering()
    {
        var enc = new AesGcmFieldEncryptor("k1", new Dictionary<string, string> { ["k1"] = Key() });
        var value = enc.Encrypt("secret");
        var raw = Convert.FromBase64String(value[3..]);
        raw[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => enc.Decrypt("k1:" + Convert.ToBase64String(raw)));
    }

    [Fact]
    public void Reads_values_written_with_a_rotated_key()
    {
        var k1 = Key();
        var old = new AesGcmFieldEncryptor("k1", new Dictionary<string, string> { ["k1"] = k1 });
        var value = old.Encrypt("secret");
        var rotated = new AesGcmFieldEncryptor("k2", new Dictionary<string, string> { ["k1"] = k1, ["k2"] = Key() });
        Assert.Equal("secret", rotated.Decrypt(value));
        Assert.StartsWith("k2:", rotated.Encrypt("x"));
    }

    [Fact]
    public void Rejects_short_keys()
        => Assert.Throws<InvalidOperationException>(() =>
            new AesGcmFieldEncryptor("k1", new Dictionary<string, string> { ["k1"] = Convert.ToBase64String(new byte[16]) }));
}

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("short1")]
    [InlineData("alllettersnodigits")]
    [InlineData("123456789012345")]
    public void Rejects_weak_passwords(string password) => Assert.NotEmpty(PasswordPolicy.Validate(password));

    [Fact]
    public void Accepts_and_verifies_strong_password()
    {
        Assert.Empty(PasswordPolicy.Validate("Correct-horse-42"));
        var hash = PasswordPolicy.Hash("Correct-horse-42");
        Assert.True(PasswordPolicy.Verify(hash, "Correct-horse-42"));
        Assert.False(PasswordPolicy.Verify(hash, "Correct-horse-43"));
        Assert.False(PasswordPolicy.Verify(null, "Correct-horse-42"));
    }
}

public class PermissionCatalogueTests
{
    [Fact]
    public void Every_system_role_permission_is_in_the_catalogue()
    {
        foreach (var role in SystemRoles.All)
        {
            Assert.All(role.Permissions, p => Assert.True(Permissions.Catalogue.ContainsKey(p), $"{role.Code}: {p}"));
        }
    }

    [Fact]
    public void Tenant_administrator_has_no_payroll_content_access()
    {
        var admin = SystemRoles.All.Single(r => r.Code == SystemRoles.TenantAdministrator);
        Assert.DoesNotContain(admin.Permissions, p => p.StartsWith("payroll.") || p.StartsWith("payments."));
    }

    [Fact]
    public void Preparer_cannot_approve_and_approver_cannot_prepare()
    {
        var preparer = SystemRoles.All.Single(r => r.Code == SystemRoles.PayrollPreparer);
        var approver = SystemRoles.All.Single(r => r.Code == SystemRoles.PayrollApprover);
        Assert.DoesNotContain(Permissions.PayrollApprove, preparer.Permissions);
        Assert.DoesNotContain(Permissions.PayrollPrepare, approver.Permissions);
    }

    [Fact]
    public void Hr_officer_has_no_payroll_approval()
        => Assert.DoesNotContain(Permissions.PayrollApprove, SystemRoles.All.Single(r => r.Code == SystemRoles.HrOfficer).Permissions);

    [Fact]
    public void Auditor_is_read_only()
    {
        var auditor = SystemRoles.All.Single(r => r.Code == SystemRoles.Auditor);
        string[] writes = [Permissions.OrgWrite, Permissions.UsersManage, Permissions.RolesManage, Permissions.PeopleWrite,
            Permissions.PayrollPrepare, Permissions.PayrollApprove, Permissions.PaymentsRelease, Permissions.LeaveApprove, Permissions.TimeApprove];
        Assert.Empty(auditor.Permissions.Intersect(writes));
    }

    [Fact]
    public void Employee_and_manager_roles_do_not_require_mfa_but_admin_does()
    {
        bool RequiresMfa(string code) => SystemRoles.All.Single(r => r.Code == code).Permissions.Any(Permissions.Privileged.Contains);
        Assert.False(RequiresMfa(SystemRoles.Employee));
        Assert.False(RequiresMfa(SystemRoles.LineManager));
        Assert.True(RequiresMfa(SystemRoles.TenantAdministrator));
        Assert.True(RequiresMfa(SystemRoles.PayrollApprover));
        Assert.True(RequiresMfa(SystemRoles.HrOfficer));
    }
}
