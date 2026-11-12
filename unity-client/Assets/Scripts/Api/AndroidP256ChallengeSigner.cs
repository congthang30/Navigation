#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Security.Cryptography;
using UnityEngine;

namespace Vps.UnityClient.Api
{
    /// <summary>Generates an ephemeral in-memory P-256 key and DER proof with Android's JCA provider.</summary>
    internal static class AndroidP256ChallengeSigner
    {
        private static readonly byte[] SpkiPrefix = {
            0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01,
            0x06, 0x08, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00
        };

        public static void Sign(byte[] message, out byte[] uncompressedPublicKey, out byte[] derSignature)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            AndroidJavaObject generator = null;
            AndroidJavaObject curve = null;
            AndroidJavaObject keyPair = null;
            AndroidJavaObject publicKey = null;
            AndroidJavaObject privateKey = null;
            AndroidJavaObject signer = null;
            AndroidJavaClass generatorType = null;
            AndroidJavaClass curveType = null;
            AndroidJavaClass signatureType = null;
            try
            {
                generatorType = new AndroidJavaClass("java.security.KeyPairGenerator");
                generator = generatorType.CallStatic<AndroidJavaObject>("getInstance", "EC");
                curveType = new AndroidJavaClass("java.security.spec.ECGenParameterSpec");
                curve = new AndroidJavaObject("java.security.spec.ECGenParameterSpec", "secp256r1");
                generator.Call("initialize", curve);
                keyPair = generator.Call<AndroidJavaObject>("generateKeyPair");
                publicKey = keyPair.Call<AndroidJavaObject>("getPublic");
                privateKey = keyPair.Call<AndroidJavaObject>("getPrivate");
                byte[] encodedPublicKey = publicKey.Call<byte[]>("getEncoded");
                if (encodedPublicKey == null || encodedPublicKey.Length != SpkiPrefix.Length + 65)
                    throw new CryptographicException("Android returned an unsupported P-256 public key encoding.");
                for (int index = 0; index < SpkiPrefix.Length; index++)
                    if (encodedPublicKey[index] != SpkiPrefix[index])
                        throw new CryptographicException("Android returned a public key for an unsupported curve.");
                uncompressedPublicKey = new byte[65];
                Buffer.BlockCopy(encodedPublicKey, SpkiPrefix.Length, uncompressedPublicKey, 0, uncompressedPublicKey.Length);
                if (uncompressedPublicKey[0] != 0x04)
                    throw new CryptographicException("Android returned a compressed EC public key.");

                signatureType = new AndroidJavaClass("java.security.Signature");
                signer = signatureType.CallStatic<AndroidJavaObject>("getInstance", "SHA256withECDSA");
                signer.Call("initSign", privateKey);
                signer.Call("update", message);
                derSignature = signer.Call<byte[]>("sign");
                if (derSignature == null || derSignature.Length < 8 || derSignature.Length > 72 || derSignature[0] != 0x30)
                    throw new CryptographicException("Android returned an invalid P-256 signature.");
            }
            finally
            {
                signer?.Dispose();
                signatureType?.Dispose();
                privateKey?.Dispose();
                publicKey?.Dispose();
                keyPair?.Dispose();
                curve?.Dispose();
                curveType?.Dispose();
                generator?.Dispose();
                generatorType?.Dispose();
            }
        }
    }
}
#endif
