using System.Text;

namespace MadWizard.Desomnia.Network.Knocking.Secrets
{
    public readonly struct SharedSecret
    {
        public byte[]   Key     { get; init; }
        public byte[]?  AuthKey { get; init; }

        public SharedSecret(byte[] key, byte[]? authKey)
        {
            Key = key;
            AuthKey = authKey;
        }

        public SharedSecret(string? key, string? authKey, string encoding)
        {
            Key = TryConvert(key, encoding) ?? [];
            AuthKey = TryConvert(authKey, encoding);
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
}
