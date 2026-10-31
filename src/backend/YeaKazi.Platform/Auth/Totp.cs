using System.Security.Cryptography;
using System.Text;

namespace YeaKazi.Platform.Auth;

/// <summary>RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30-second steps) with replay protection by step.</summary>
public static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;
    public const int AllowedDriftSteps = 1;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(byte[] secret, long step, int digits = Digits)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(step & 0xff);
            step >>= 8;
        }

        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24)
                     | (hash[offset + 1] << 16)
                     | (hash[offset + 2] << 8)
                     | hash[offset + 3];
        var modulo = (int)Math.Pow(10, digits);
        return (binary % modulo).ToString(new string('0', digits));
    }

    /// <summary>
    /// Returns the matched step, or null. A code is accepted only for a step later than
    /// <paramref name="lastUsedStep"/>, so a code cannot be replayed.
    /// </summary>
    public static long? Verify(byte[] secret, string? code, DateTimeOffset now, long lastUsedStep)
    {
        if (code is null || code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return null;
        }

        var current = StepAt(now);
        for (var drift = -AllowedDriftSteps; drift <= AllowedDriftSteps; drift++)
        {
            var step = current + drift;
            if (step <= lastUsedStep)
            {
                continue;
            }

            var expected = Encoding.ASCII.GetBytes(Compute(secret, step));
            if (CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(code)))
            {
                return step;
            }
        }

        return null;
    }

    public static string OtpAuthUri(string issuer, string account, byte[] secret)
        => $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={Base32.Encode(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
}

public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return sb.ToString();
    }

    public static byte[] Decode(string text)
    {
        var clean = text.Trim().TrimEnd('=').Replace(" ", string.Empty).ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0)
            {
                throw new FormatException("Invalid base32 character.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}
