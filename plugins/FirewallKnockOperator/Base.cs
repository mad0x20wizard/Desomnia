using MadWizard.Desomnia.Network.Knocking.Secrets;
using MadWizard.Desomnia.Network.Services.Knocking;
using System.Security.Cryptography;
using System.Text;

namespace MadWizard.Desomnia.Network.FirewallKnockOperator
{
    internal abstract class Base : IKnockValidation
    {
        const int IV_LENGTH = 16;

        protected const int SALT_LENGTH = 8;

        protected const string SALT_PREFIX = "Salted__";
        protected const string SALT_PREFIX_BASE64 = "U2FsdGVkX1";

        protected static readonly byte[] SALT_PREFIX_BYTES = Encoding.ASCII.GetBytes(SALT_PREFIX);

        #region Bas64 Encoding/Decoding (fwknop style)
        internal static byte[] DecodeBase64(string input)
        {
            // Remove whitespace
            input = input.Trim();

            // Add padding if missing
            int missingPadding = input.Length % 4;
            if (missingPadding > 0)
            {
                input += new string('=', 4 - missingPadding);
            }

            return Convert.FromBase64String(input);
        }

        internal static string EncodeBase64(byte[] bytes) => Convert.ToBase64String(bytes).Replace("=", "");

        internal static string EncodeBase64Str(string s) => EncodeBase64(Encoding.UTF8.GetBytes(s));
        internal static string DecodeBase64Str(string s) => Encoding.UTF8.GetString(DecodeBase64(s));
        #endregion

        void IKnockValidation.ValidateSecret(SharedSecret secret) => ValidateAESSecret(secret);

        /// <summary>
        /// The shared secret IS the AES key: every call site derives with <c>keyLen: secret.Key.Length</c>,
        /// so the secret's byte count becomes the cipher's key size. Anything other than 16/24/32 bytes
        /// fails deep inside AesImplementation as "Specified key is not a valid size for this algorithm",
        /// naming neither the secret nor the cause — hence this check, wired into
        /// <c>IKnockMethod.ValidateSecret</c> / <c>IKnockDetector.ValidateSecret</c> so it runs before
        /// the first knock and at startup respectively.
        /// </summary>
        protected static void ValidateAESSecret(SharedSecret secret)
        {
            if (secret.Key.Length is 16 or 24 or 32)
                return;

            throw new CryptographicException(
                $"The knock secret is {secret.Key.Length} bytes long; fwknop requires 16, 24 or 32 bytes " +
                 "(AES-128/192/256). A base64-encoded secret needs knockSecretEncoding=\"Base64\" — without " +
                 "it the secret's text is used verbatim, so a 44-character base64 key yields 44 bytes.");
        }

        protected static (byte[] key, byte[] iv) DeriveKeyIV(byte[] passphrase, byte[] salt, int keyLen, int ivLen = IV_LENGTH)
        {
            // OpenSSL EVP_BytesToKey with MD5, 1 iteration (PBKDF1)

            byte[] prev = [];
            byte[] derived = [];
            while (derived.Length < keyLen + ivLen)
            {
                byte[] data = [.. prev, .. passphrase, .. salt];

                derived = [.. derived, .. (prev = MD5.HashData(data))];
            }

            byte[] key = [.. derived.Take(keyLen)];
            byte[] iv = [.. derived.Skip(keyLen).Take(ivLen)];

            return (key, iv);
        }

        protected static HMAC? AuthMethod(SharedSecret secret)
        {
            if (secret.AuthKey is byte[] key)
            {
                switch (secret.AuthType)
                {
                    case DigestType.MD5:
                        return new HMACMD5(key);

                    case DigestType.SHA1:
                        return new HMACSHA1(key);

                    case DigestType.SHA256:
                    case DigestType.Default:
                        return new HMACSHA256(key);
                    case DigestType.SHA384:
                        return new HMACSHA384(key);
                    case DigestType.SHA512:
                        return new HMACSHA512(key);

                    case DigestType.SHA3_256:
                        return new HMACSHA3_256(key);
                    case DigestType.SHA3_512:
                        return new HMACSHA3_512(key);

                    default:
                        throw new NotImplementedException(secret.AuthType.ToString());
                }
            }

            return null;
        }

        protected static byte[] CalculateHMAC(string cipherB64, HMAC? auth)
        {
            return auth?.ComputeHash(Encoding.ASCII.GetBytes(cipherB64)) ?? [];
        }
    }
}
