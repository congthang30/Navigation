using System;
using System.Security.Cryptography;
using System.Text;

namespace Vps.UnityClient.Api
{
    [Serializable]
    public sealed class DeviceChallengeProof
    {
        public int version;
        public string nonce;
        public long issuedAt;
        public string publicKey;
        public string signature;
    }

    [Serializable]
    internal sealed class GuestChallengeRequest
    {
        public DeviceChallengeProof challenge;
    }

    /// <summary>Creates a one-use, memory-only P-256 proof accepted by the mobile API.</summary>
    public static class DeviceChallengeSigner
    {
        private const string ChallengeDomain = "vps-indoor-ar-guest-challenge-v2";

        public static string CreateRequestJson(DateTime utcNow)
        {
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                byte[] publicKeyBytes;
                byte[] signature;
                byte[] nonceBytes = new byte[32];
                random.GetBytes(nonceBytes);
                long issuedAt = new DateTimeOffset(utcNow.ToUniversalTime()).ToUnixTimeSeconds();
                string nonce = Base64Url(nonceBytes);
                byte[] message = Encoding.UTF8.GetBytes(ChallengeDomain + "\n2\n" + issuedAt + "\n" + nonce);
#if UNITY_ANDROID && !UNITY_EDITOR
                AndroidP256ChallengeSigner.Sign(message, out publicKeyBytes, out signature);
#else
                using (ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    ECParameters parameters = key.ExportParameters(false);
                if (parameters.Q.X == null || parameters.Q.Y == null ||
                    parameters.Q.X.Length != 32 || parameters.Q.Y.Length != 32)
                {
                    throw new CryptographicException("The platform returned an invalid P-256 public key.");
                }

                    publicKeyBytes = new byte[65];
                    publicKeyBytes[0] = 0x04;
                    Buffer.BlockCopy(parameters.Q.X, 0, publicKeyBytes, 1, 32);
                    Buffer.BlockCopy(parameters.Q.Y, 0, publicKeyBytes, 33, 32);
                    signature = ToDerSignature(key.SignData(message, HashAlgorithmName.SHA256));
                }
#endif
                return SerializeProof(nonceBytes, issuedAt, publicKeyBytes, signature);
            }
        }

        public static string SerializeProof(byte[] nonceBytes, long issuedAt, byte[] publicKeyBytes, byte[] derSignature)
        {
            if (nonceBytes == null || nonceBytes.Length != 32 || publicKeyBytes == null || publicKeyBytes.Length != 65 || publicKeyBytes[0] != 0x04)
                throw new CryptographicException("A canonical 32-byte nonce and uncompressed P-256 public key are required.");
            if (derSignature == null || derSignature.Length < 8 || derSignature.Length > 72)
                throw new CryptographicException("A DER encoded P-256 signature is required.");
            byte[] signature = ToDerSignature(derSignature);
            var request = new GuestChallengeRequest
            {
                    challenge = new DeviceChallengeProof
                    {
                        version = 2,
                        nonce = Base64Url(nonceBytes),
                        issuedAt = issuedAt,
                        publicKey = Base64Url(publicKeyBytes),
                        signature = Base64Url(signature)
                    }
                };
            return Newtonsoft.Json.JsonConvert.SerializeObject(request);
        }

        private static byte[] ToDerSignature(byte[] signature)
        {
            byte[] raw = signature.Length == 64 ? signature : ParseDerSignature(signature);
            byte[] r = EncodeInteger(raw, 0);
            byte[] s = EncodeInteger(raw, 32);
            byte[] body = new byte[r.Length + s.Length];
            Buffer.BlockCopy(r, 0, body, 0, r.Length);
            Buffer.BlockCopy(s, 0, body, r.Length, s.Length);
            byte[] result = new byte[body.Length + 2];
            result[0] = 0x30;
            result[1] = checked((byte)body.Length);
            Buffer.BlockCopy(body, 0, result, 2, body.Length);
            return result;
        }

        private static byte[] ParseDerSignature(byte[] signature)
        {
            if (signature == null || signature.Length < 8 || signature[0] != 0x30)
                throw new CryptographicException("ECDSA returned an unsupported signature format.");
            int offset = 1;
            int sequenceLength = ReadLength(signature, ref offset);
            if (sequenceLength != signature.Length - offset) throw new CryptographicException("Malformed ECDSA signature.");
            byte[] raw = new byte[64];
            ReadInteger(signature, ref offset, raw, 0);
            ReadInteger(signature, ref offset, raw, 32);
            if (offset != signature.Length) throw new CryptographicException("Malformed ECDSA signature.");
            return raw;
        }

        private static int ReadLength(byte[] data, ref int offset)
        {
            if (offset >= data.Length) throw new CryptographicException("Malformed ECDSA signature.");
            int first = data[offset++];
            if ((first & 0x80) == 0) return first;
            int count = first & 0x7f;
            if (count < 1 || count > 2 || offset + count > data.Length) throw new CryptographicException("Malformed ECDSA signature.");
            int length = 0;
            for (int index = 0; index < count; index++) length = (length << 8) | data[offset++];
            return length;
        }

        private static void ReadInteger(byte[] data, ref int offset, byte[] output, int outputOffset)
        {
            if (offset >= data.Length || data[offset++] != 0x02) throw new CryptographicException("Malformed ECDSA signature.");
            int length = ReadLength(data, ref offset);
            if (length < 1 || offset + length > data.Length) throw new CryptographicException("Malformed ECDSA signature.");
            int leadingZeros = 0;
            while (leadingZeros < length - 1 && data[offset + leadingZeros] == 0) leadingZeros++;
            int valueLength = length - leadingZeros;
            if (valueLength > 32) throw new CryptographicException("Malformed ECDSA signature integer.");
            Buffer.BlockCopy(data, offset + leadingZeros, output, outputOffset + 32 - valueLength, valueLength);
            offset += length;
        }

        private static byte[] EncodeInteger(byte[] raw, int offset)
        {
            int first = offset;
            while (first < offset + 31 && raw[first] == 0) first++;
            bool needsSignByte = (raw[first] & 0x80) != 0;
            int valueLength = 32 - (first - offset) + (needsSignByte ? 1 : 0);
            byte[] encoded = new byte[valueLength + 2];
            encoded[0] = 0x02;
            encoded[1] = checked((byte)valueLength);
            Buffer.BlockCopy(raw, first, encoded, needsSignByte ? 3 : 2, 32 - (first - offset));
            return encoded;
        }

        private static string Base64Url(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
