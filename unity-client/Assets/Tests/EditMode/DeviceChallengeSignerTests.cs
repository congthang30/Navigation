using System;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Vps.UnityClient.Api;

namespace Vps.UnityClient.Tests.EditMode
{
    public sealed class DeviceChallengeSignerTests
    {
        [Test]
        public void CreatesCanonicalP256GuestChallengeWithoutPersistingPrivateKey()
        {
            byte[] nonceFixture = new byte[32];
            byte[] publicKeyFixture = new byte[65];
            publicKeyFixture[0] = 0x04;
            byte[] derSignatureFixture = { 0x30, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x01 };
            string json = DeviceChallengeSigner.SerializeProof(
                nonceFixture, 1793592000, publicKeyFixture, derSignatureFixture);
            JObject root = JObject.Parse(json);
            JObject challenge = (JObject)root["challenge"];
            byte[] nonce = FromBase64Url((string)challenge["nonce"]);
            byte[] publicKey = FromBase64Url((string)challenge["publicKey"]);
            byte[] signature = FromBase64Url((string)challenge["signature"]);

            Assert.That((int)challenge["version"], Is.EqualTo(2));
            Assert.That(nonce, Has.Length.EqualTo(32));
            Assert.That(publicKey, Has.Length.EqualTo(65));
            Assert.That(publicKey[0], Is.EqualTo(0x04));
            Assert.That(signature.Length, Is.InRange(8, 72));
            Assert.That(signature[0], Is.EqualTo(0x30));
            Assert.That(json, Does.Not.Contain("privateKey"));
        }

        private static byte[] FromBase64Url(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            return Convert.FromBase64String(padded);
        }
    }
}
