using System.Security.Cryptography;
using System.Text;

namespace MadWizard.Desomnia.Network.FirewallKnockOperator
{
    internal abstract class FKO
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

        protected static (byte[] key, byte[] iv) DeriveKeyIV(byte[] passphrase, byte[] salt, int keyLen, int ivLen = IV_LENGTH)
        {
            // OpenSSL EVP_BytesToKey with MD5, 1 iteration

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
    }
}
