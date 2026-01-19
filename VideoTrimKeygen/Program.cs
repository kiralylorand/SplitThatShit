using System.Security.Cryptography;
using System.Text;

namespace VideoTrimKeygen;

internal static class Program
{
    // MUST match LicenseManager.SecretSalt in the main app.
    private const string SecretSalt = "VT_LIC_2025_SECRET_1";

    private static void Main(string[] args)
    {
        Console.WriteLine("Split That Sh!t License Key Generator");
        Console.WriteLine("Format: VT-XXXX-XXXX-XXXX");
        Console.WriteLine("Press Enter to generate a new key, or type 'q' and Enter to quit.");
        Console.WriteLine();

        while (true)
        {
            Console.Write("Command: ");
            var line = Console.ReadLine();
            if (line != null && line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var key = GenerateKey();
            Console.WriteLine("License key: " + key);
        }
    }

    private static string GenerateKey()
    {
        using var rng = RandomNumberGenerator.Create();

        string RandomBlock()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // avoid 0/O/1/I
            Span<byte> bytes = stackalloc byte[4];
            rng.GetBytes(bytes);
            var sb = new StringBuilder(4);
            for (var i = 0; i < 4; i++)
            {
                sb.Append(chars[bytes[i] % chars.Length]);
            }

            return sb.ToString();
        }

        var block1 = RandomBlock();
        var block2 = RandomBlock();
        var body = $"VT-{block1}-{block2}";
        var checksum = ComputeChecksum(body);
        return $"{body}-{checksum}";
    }

    private static string ComputeChecksum(string body)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(SecretSalt + "|" + body);
        var hash = sha.ComputeHash(bytes);

        var sb = new StringBuilder(4);
        for (var i = 0; i < 2; i++)
        {
            sb.Append(hash[i].ToString("X2"));
        }

        return sb.ToString();
    }
}

