using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Knocking.Secrets;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Security.Cryptography;
using System.Text;

namespace MadWizard.Desomnia.Network.FirewallKnockOperator
{
    internal class FKOReceiver : FKO, IKnockDetector
    {
        public required ILogger<FKOSender> Logger { private get; init; }

        IEnumerable<KnockEvent> IKnockDetector.Examine(IPPacket packet, SharedSecret secret)
        {
            if (packet.PayloadPacket is TransportPacket transport)
            {
                FKOData data;
                try
                {
                    var bytes = TransformPayload(transport.PayloadData);

                    if (secret.AuthKey is byte[] hmacKey)
                    {
                        using var hm = new HMACSHA256(hmacKey);

                        byte[] hmac = bytes[^32..]; bytes = bytes[..^32]; // chop off 32 bytes HMAC-SHA256

                        // 4) verify HMAC: HMAC_SHA256( salt || ct )
                        if (!hmac.SequenceEqual(hm.ComputeHash(Encoding.ASCII.GetBytes(EncodeBase64(bytes)))))
                        {
                            throw new CryptographicException("HMAC verification FAILED.");
                        }
                    }

                    byte[] salt = [.. bytes.Skip(SALT_PREFIX_BYTES.Length).Take(SALT_LENGTH)];
                    byte[] ciphertext = [.. bytes.Skip(SALT_PREFIX_BYTES.Length + SALT_LENGTH)];

                    (byte[] key, byte[] iv) = DeriveKeyIV(secret.Key, salt, secret.Key.Length);

                    string plaintext = Encoding.ASCII.GetString(DecryptAES(ciphertext, key, iv));

                    // TODO: verify HMAC if secret.AuthKey is set
                    // TODO: verify plaintext

                    data = new FKOData(plaintext);
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "Failed to decrypt/analyze FKO packet");

                    yield break;
                }

                yield return new KnockEvent
                {
                    Time = data.Timestamp.DateTime,
                    SourceAddress = packet.SourceAddress, // data.SourceAddress, // TODO: implement public IP resolution
                    TargetPort = data.TargetPort,
                };
            }
        }

        static byte[] TransformPayload(byte[] bytes)
        {
            var str = Encoding.ASCII.GetString(bytes);

            if (!str.StartsWith(SALT_PREFIX_BASE64))
            {
                str = SALT_PREFIX_BASE64 + str;
            }

            return DecodeBase64(str);
        }

        static byte[] DecryptAES(byte[] ciphertext, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using (var dec = aes.CreateDecryptor(key, iv))
            {
                return dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }
        }
    }
}
