using System.Security.Cryptography;
using System.Text;

namespace MadWizard.Desomnia.Network.Knocking.Secrets
{
    public class SharedSecret
    {
        public byte[]       Key         { get; init; }
        public byte[]?      AuthKey     { get; init; }
        public DigestType   AuthType    { get; init; }

        public SharedSecret(byte[] key, byte[]? authKey, DigestType authType)
        {
            Key = key;
            AuthKey = authKey;
            AuthType = authType;
        }

        public SharedSecret(string? key, string? authKey, DigestType authType, string encoding)
        {
            Key = TryConvert(key, encoding) ?? [];
            AuthKey = TryConvert(authKey, encoding);
            AuthType = authType;
        }

        internal static byte[]? TryConvert(string? str, string encoding)
        {
            if (str is not null)
            {
                if (encoding.Equals("base64", StringComparison.InvariantCultureIgnoreCase))
                {
                    return Convert.FromBase64String(str);
                }
                else
                {
                    return Encoding.GetEncoding(encoding).GetBytes(str);
                }
            }

            return null;
        }
    }

    public enum DigestType
    {
        Default = 0,

        MD5,
        SHA1,
        SHA256,
        SHA384,
        SHA512,

        // are they for real?
        SHA3_256,
        SHA3_512
    }
}
