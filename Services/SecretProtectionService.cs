using System.Security.Cryptography;
using System.Text;

namespace ourmclauncher.Services;

internal static class SecretProtectionService
{
    private static readonly byte[] Entropy = SHA256.HashData(
        Encoding.UTF8.GetBytes("OMLLauncher.LocalSecrets.v1"));

    public static string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            var protectedData = ProtectedData.Protect(
                plaintext,
                Entropy,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static bool TryUnprotect(string? protectedValue, out string value)
    {
        value = "";
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return true;
        }

        try
        {
            var encryptedData = Convert.FromBase64String(protectedValue);
            var plaintext = ProtectedData.Unprotect(
                encryptedData,
                Entropy,
                DataProtectionScope.CurrentUser);
            try
            {
                value = Encoding.UTF8.GetString(plaintext);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }
}
