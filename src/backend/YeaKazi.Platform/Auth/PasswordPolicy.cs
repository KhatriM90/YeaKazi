using Microsoft.AspNetCore.Identity;

namespace YeaKazi.Platform.Auth;

/// <summary>Password rules and hashing (ASP.NET Core Identity PBKDF2 hasher).</summary>
public sealed class PasswordPolicy
{
    public const int MinimumLength = 12;
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object Subject = new();

    // Pre-computed hash used to equalize timing for unknown accounts.
    private static readonly string DummyHash = Hasher.HashPassword(Subject, "dummy-password-for-timing");

    public static IReadOnlyList<string> Validate(string? password)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
        {
            errors.Add($"Password must be at least {MinimumLength} characters.");
            return errors;
        }

        if (password.Length > 256)
        {
            errors.Add("Password must be at most 256 characters.");
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            errors.Add("Password must contain letters and digits.");
        }

        return errors;
    }

    public static string Hash(string password) => Hasher.HashPassword(Subject, password);

    public static bool Verify(string? hash, string password)
    {
        if (string.IsNullOrEmpty(hash))
        {
            Hasher.VerifyHashedPassword(Subject, DummyHash, password);
            return false;
        }

        return Hasher.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;
    }

    public static void BurnTime(string password) => Hasher.VerifyHashedPassword(Subject, DummyHash, password);
}
