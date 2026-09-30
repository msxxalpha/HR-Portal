using System.Security.Cryptography;
namespace HRPortal.Services;

public static class PasswordHasher
{
    public static string Hash(string value) { var salt = RandomNumberGenerator.GetBytes(16); var hash = Rfc2898DeriveBytes.Pbkdf2(value, salt, 100000, HashAlgorithmName.SHA256, 32); return $"100000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}"; }
    public static bool Verify(string value, string stored) { try { var p = stored.Split('.'); var salt = Convert.FromBase64String(p[1]); var expected = Convert.FromBase64String(p[2]); var hash = Rfc2898DeriveBytes.Pbkdf2(value, salt, int.Parse(p[0]), HashAlgorithmName.SHA256, expected.Length); return CryptographicOperations.FixedTimeEquals(hash, expected); } catch { return false; } }
}