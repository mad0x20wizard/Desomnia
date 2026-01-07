using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Knocking.Secrets;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace MadWizard.Desomnia.Network.FirewallKnockOperator
{
    internal class FKOSender : FKO, IKnockMethod
    {
        public required ILogger<FKOSender> Logger { private get; init; }

        void IKnockMethod.Knock(IPAddress source, IPEndPoint target, IPPort knock, SharedSecret secret)
        {
            using UdpClient udp = new(target.AddressFamily);

            Logger.LogTrace($"Knocking at {target} using {knock.Port}/udp");

            var payload = BuildFKOPayload(secret, source, target.ToIPPort());

            var bytes = udp.Send(payload, new IPEndPoint(target.Address, knock.Port));
        }

        static byte[] BuildFKOPayload(SharedSecret secret, IPAddress source, IPPort? target, string username = "desomnia")
        {
            // 1) Build the plaintext fields (without the trailing digest yet)
            var data = new FKOData(source, target) { Username = username };
            // 2) Compute the plaintext digest (SHA256) over the body
            data.Digest = SHA256.HashData(Encoding.UTF8.GetBytes(data.ToMessageString()));

            byte[] salt = GenerateSalt(); // 8 bytes salt

            var (key, iv) = DeriveKeyIV(secret.Key, salt, keyLen: secret.Key.Length); // AES-256-CBC

            byte[] plaintext    = Encoding.ASCII.GetBytes(data.ToString());
            byte[] ciphertext   = EncryptAES(plaintext, key, iv);

            string b64Cipher    = EncodeBase64([.. SALT_PREFIX_BYTES, .. salt, .. ciphertext]);
            string b64HMAC      = EncodeBase64(CalculateHMAC(b64Cipher, secret.AuthKey));  // optional

            return TransformPayload(b64Cipher + b64HMAC);
        }

        static byte[] TransformPayload(string str)
        {
            if (str.StartsWith(SALT_PREFIX_BASE64))
            {
                str = str[SALT_PREFIX_BASE64.Length..];
            }

            return Encoding.ASCII.GetBytes(str);
        }

        static byte[] GenerateSalt()
        {
            byte[] salt = new byte[SALT_LENGTH];
            RandomNumberGenerator.Fill(salt);
            return salt;
        }

        static byte[] EncryptAES(byte[] plaintext, byte[] key, byte[]? iv = null)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.KeySize = key.Length * 8; // 128/192/256

            using (var encryptor = aes.CreateEncryptor(key, iv))
            {
                return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }
        }

        static byte[] CalculateHMAC(string cipherB64, byte[]? hmacKey)
        {
            if (hmacKey != null)
            {
                using var hmac = new HMACSHA256(hmacKey);

                return hmac.ComputeHash(Encoding.ASCII.GetBytes(cipherB64));
            }

            return [];
        }
    }
}
