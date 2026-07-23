
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AEMCM.Orchestrator.Tests.TestHelpers
{
    /// <summary>
    /// Builds self-signed / CA-signed PFX blobs for tests using only the BCL
    /// (System.Security.Cryptography). No BouncyCastle dependency.
    /// </summary>
    public static class CertTestFactory
    {
        public const string DefaultPassword = "test-password";

        /// <summary>Self-signed RSA leaf (no chain).</summary>
        public static byte[] CreateSelfSignedRsaPfx(
            string commonName, string[] sans, int keySize = 2048, string password = DefaultPassword)
        {
            using var rsa = RSA.Create(keySize);
            var req = new CertificateRequest(
                $"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            AddLeafExtensions(req, sans);
            using var cert = req.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return cert.Export(X509ContentType.Pkcs12, password);
        }

        /// <summary>Self-signed ECDSA leaf (no chain). Curve: P-256 (256) or P-384 (384).</summary>
        public static byte[] CreateSelfSignedEcdsaPfx(
            string commonName, string[] sans, int keyBits = 256, string password = DefaultPassword)
        {
            var curve = keyBits == 384 ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP256;
            using var ecdsa = ECDsa.Create(curve);
            var req = new CertificateRequest($"CN={commonName}", ecdsa, HashAlgorithmName.SHA256);
            AddLeafExtensions(req, sans);
            using var cert = req.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return cert.Export(X509ContentType.Pkcs12, password);
        }

        /// <summary>RSA leaf signed by a generated CA, producing a two-cert chain in the PFX.</summary>
        public static byte[] CreateChainedRsaPfx(
            string commonName, string[] sans, int keySize = 2048, string password = DefaultPassword)
        {
            using var caRsa = RSA.Create(2048);
            var caReq = new CertificateRequest(
                "CN=AEMCM Test Root CA", caRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using var caCert = caReq.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddYears(10));

            using var leafRsa = RSA.Create(keySize);
            var leafReq = new CertificateRequest(
                $"CN={commonName}", leafRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            AddLeafExtensions(leafReq, sans);

            var serial = new byte[8];
            RandomNumberGenerator.Fill(serial);
            using var leafPublic = leafReq.Create(
                caCert, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), serial);
            using var leafWithKey = leafPublic.CopyWithPrivateKey(leafRsa);

#if NET9_0_OR_GREATER
            using var caPublic = X509CertificateLoader.LoadCertificate(caCert.RawData);
#else
            using var caPublic = new X509Certificate2(caCert.RawData); // public-only CA
#endif
            var collection = new X509Certificate2Collection { leafWithKey, caPublic };
            return collection.Export(X509ContentType.Pkcs12, password)!;
        }

        private static void AddLeafExtensions(CertificateRequest req, string[] sans)
        {
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            var sanBuilder = new SubjectAlternativeNameBuilder();
            foreach (var s in sans) sanBuilder.AddDnsName(s);
            req.CertificateExtensions.Add(sanBuilder.Build());
        }
    }
}
