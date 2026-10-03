using System.Security.Cryptography;
namespace HRPortal.Services;

public static class PasswordHasher
{
    public const int PersonalPasswordMinimumLength = 6;

    public static bool IsValidPersonalPassword(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length >= PersonalPasswordMinimumLength;

    public static string Hash(string value)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(value, salt, 100000, HashAlgorithmName.SHA256, 32);
        return $"100000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string value, string stored)
    {
        try
        {
            var parts = stored.Split('.');
            if (parts.Length != 3 ||
                string.IsNullOrEmpty(parts[0]) ||
                string.IsNullOrEmpty(parts[1]) ||
                string.IsNullOrEmpty(parts[2]))
                return false;

            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var iterations = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            var hash = Rfc2898DeriveBytes.Pbkdf2(value, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(hash, expected);
        }
        catch
        {
            return false;
        }
    }
}