using System;
using System.Security.Cryptography;
using System.Text;

namespace VideoTrim;

internal static class LicenseManager
{
    // Keep this secret private – used both for generating and validating keys.
    private const string SecretSalt = "VT_LIC_2025_SECRET_1";

    public static bool IsLicensed(AppSettings settings)
        => settings != null && settings.IsLicensed && Validate(settings.LicenseKey);

    public static bool Validate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        key = key.Trim().ToUpperInvariant();
        if (!IsFormatValid(key))
        {
            return false;
        }

        return IsSignatureValid(key);
    }

    public static bool IsFormatValid(string key)
    {
        // Expected: VT-XXXX-XXXX-XXXX (letters/digits)
        if (!key.StartsWith("VT-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = key.Split('-');
        if (parts.Length != 4)
        {
            return false;
        }

        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length != 4)
            {
                return false;
            }

            foreach (var ch in parts[i])
            {
                if (!char.IsLetterOrDigit(ch))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsSignatureValid(string key)
    {
        // Body = everything except last group; last group = checksum.
        var parts = key.Split('-');
        var body = string.Join("-", parts[0], parts[1], parts[2]);
        var expected = parts[3];

        var actual = ComputeChecksum(body);
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    public static string ComputeChecksum(string body)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(SecretSalt + "|" + body);
        var hash = sha.ComputeHash(bytes);

        // Take first 4 hex chars as signature.
        var sb = new StringBuilder(4);
        for (var i = 0; i < 2; i++)
        {
            sb.Append(hash[i].ToString("X2"));
        }

        return sb.ToString();
    }
}

