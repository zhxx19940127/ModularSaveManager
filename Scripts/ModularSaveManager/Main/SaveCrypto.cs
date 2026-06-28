using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 存档加解密工具。
///
/// 设计目标：
/// - 保存时把 JSON 明文加密成普通玩家不容易直接修改的文本。
/// - 读取时通过固定文件头识别是否是加密存档。
/// - 使用 HMAC 校验内容是否被篡改，避免错误密钥或改档内容被当成正常 JSON 使用。
///
/// 注意：
/// 本地加密只能提高改档门槛，不能提供服务端级别的绝对安全。
/// 因为客户端代码和密钥最终都在玩家设备上，高手仍然可能逆向。
/// </summary>
public static class SaveCrypto
{
    /// <summary>
    /// 加密存档文件头。
    /// 读取时通过这个前缀判断文件是不是由 SaveCrypto 加密过。
    /// </summary>
    public const string EncryptedPrefix = "SMENC1:";

    private const int SaltByteCount = 16;
    private const int IvByteCount = 16;
    private const int AesKeyByteCount = 32;
    private const int HmacKeyByteCount = 32;
    private const int HmacByteCount = 32;
    private const int DeriveIterations = 10000;

    /// <summary>
    /// 判断一段文件内容是否是加密存档文本。
    /// </summary>
    public static bool IsEncryptedText(string text)
    {
        return !string.IsNullOrEmpty(text) && text.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// 把 JSON 明文加密成可写入文件的文本。
    ///
    /// 输出格式：
    /// SMENC1:Base64(salt + iv + cipher + hmac)
    ///
    /// salt 用于派生密钥，iv 用于 AES-CBC，hmac 用于校验内容完整性。
    /// </summary>
    public static string Encrypt(string plainText, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            Debug.LogError("存档加密失败: 密钥不能为空");
            return plainText;
        }

        try
        {
            byte[] salt = CreateRandomBytes(SaltByteCount);
            byte[] iv = CreateRandomBytes(IvByteCount);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText ?? string.Empty);
            byte[] aesKey;
            byte[] hmacKey;

            DeriveKeys(password, salt, out aesKey, out hmacKey);

            byte[] cipherBytes = EncryptAesCbc(plainBytes, aesKey, iv);
            byte[] payloadWithoutHmac = Combine(salt, iv, cipherBytes);
            byte[] hmac = ComputeHmac(payloadWithoutHmac, hmacKey);
            byte[] payload = Combine(payloadWithoutHmac, hmac);

            return string.Concat(EncryptedPrefix, Convert.ToBase64String(payload));
        }
        catch (Exception exception)
        {
            Debug.LogError($"存档加密失败: error={exception.Message}");
            return plainText;
        }
    }

    /// <summary>
    /// 尝试把加密文本解密回 JSON 明文。
    ///
    /// 如果内容不是加密格式、密钥错误、内容被篡改或格式损坏，会返回 false。
    /// </summary>
    public static bool TryDecrypt(string encryptedText, string password, out string plainText)
    {
        plainText = string.Empty;

        if (!IsEncryptedText(encryptedText))
        {
            Debug.LogError("存档解密失败: 文件内容不是加密格式");
            return false;
        }

        if (string.IsNullOrEmpty(password))
        {
            Debug.LogError("存档解密失败: 密钥不能为空");
            return false;
        }

        try
        {
            string base64 = encryptedText.Substring(EncryptedPrefix.Length);
            byte[] payload = Convert.FromBase64String(base64);
            int minLength = SaltByteCount + IvByteCount + HmacByteCount + 1;

            if (payload.Length < minLength)
            {
                Debug.LogError("存档解密失败: 加密内容长度无效");
                return false;
            }

            byte[] salt = Slice(payload, 0, SaltByteCount);
            byte[] iv = Slice(payload, SaltByteCount, IvByteCount);
            int cipherLength = payload.Length - SaltByteCount - IvByteCount - HmacByteCount;
            byte[] cipherBytes = Slice(payload, SaltByteCount + IvByteCount, cipherLength);
            byte[] storedHmac = Slice(payload, payload.Length - HmacByteCount, HmacByteCount);
            byte[] aesKey;
            byte[] hmacKey;

            DeriveKeys(password, salt, out aesKey, out hmacKey);

            byte[] payloadWithoutHmac = Combine(salt, iv, cipherBytes);
            byte[] expectedHmac = ComputeHmac(payloadWithoutHmac, hmacKey);
            if (!FixedTimeEquals(storedHmac, expectedHmac))
            {
                Debug.LogError("存档解密失败: 密钥错误或文件被篡改");
                return false;
            }

            byte[] plainBytes = DecryptAesCbc(cipherBytes, aesKey, iv);
            plainText = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"存档解密失败: error={exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// 用安全随机数生成 salt 或 iv。
    /// </summary>
    private static byte[] CreateRandomBytes(int count)
    {
        byte[] bytes = new byte[count];
        using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
        {
            generator.GetBytes(bytes);
        }

        return bytes;
    }

    /// <summary>
    /// 从用户密钥和 salt 派生 AES 密钥与 HMAC 密钥。
    /// </summary>
    private static void DeriveKeys(string password, byte[] salt, out byte[] aesKey, out byte[] hmacKey)
    {
        using (Rfc2898DeriveBytes deriveBytes = new Rfc2898DeriveBytes(password, salt, DeriveIterations))
        {
            aesKey = deriveBytes.GetBytes(AesKeyByteCount);
            hmacKey = deriveBytes.GetBytes(HmacKeyByteCount);
        }
    }

    /// <summary>
    /// 使用 AES-CBC 加密字节数据。
    /// </summary>
    private static byte[] EncryptAesCbc(byte[] plainBytes, byte[] key, byte[] iv)
    {
        using (Aes aes = Aes.Create())
        {
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;

            using (ICryptoTransform encryptor = aes.CreateEncryptor())
            {
                return encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            }
        }
    }

    /// <summary>
    /// 使用 AES-CBC 解密字节数据。
    /// </summary>
    private static byte[] DecryptAesCbc(byte[] cipherBytes, byte[] key, byte[] iv)
    {
        using (Aes aes = Aes.Create())
        {
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;

            using (ICryptoTransform decryptor = aes.CreateDecryptor())
            {
                return decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            }
        }
    }

    /// <summary>
    /// 计算 HMACSHA256，用于校验密文是否被篡改。
    /// </summary>
    private static byte[] ComputeHmac(byte[] bytes, byte[] key)
    {
        using (HMACSHA256 hmac = new HMACSHA256(key))
        {
            return hmac.ComputeHash(bytes);
        }
    }

    /// <summary>
    /// 拼接多段字节数组。
    /// </summary>
    private static byte[] Combine(params byte[][] arrays)
    {
        int totalLength = 0;
        for (int i = 0; i < arrays.Length; i++)
        {
            totalLength += arrays[i].Length;
        }

        byte[] result = new byte[totalLength];
        int offset = 0;
        for (int i = 0; i < arrays.Length; i++)
        {
            Buffer.BlockCopy(arrays[i], 0, result, offset, arrays[i].Length);
            offset += arrays[i].Length;
        }

        return result;
    }

    /// <summary>
    /// 从字节数组中截取一段。
    /// </summary>
    private static byte[] Slice(byte[] bytes, int offset, int length)
    {
        byte[] result = new byte[length];
        Buffer.BlockCopy(bytes, offset, result, 0, length);
        return result;
    }

    /// <summary>
    /// 固定时间比较，降低 HMAC 比较中的时序信息泄露。
    /// </summary>
    private static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }

        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }

        return diff == 0;
    }
}
