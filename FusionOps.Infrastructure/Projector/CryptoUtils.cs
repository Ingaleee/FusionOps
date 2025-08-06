using System.Security.Cryptography;
using System.Text;

namespace FusionOps.Infrastructure.Projector;

public static class CryptoUtils
{
    public static byte[] EncryptAes(string plainText, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        return encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
    }

    public static string HashActor(string actor)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(actor);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}