
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.X509;

namespace Keyfactor.Extensions.Orchestrator.AEMCM.Logic
{
    /// <summary>Result of splitting a PFX into the parts Cloud Manager expects.</summary>
    public sealed class SplitCertificate
    {
        /// <summary>Leaf certificate, PEM-encoded.</summary>
        public string CertificatePem { get; init; } = string.Empty;

        /// <summary>Private key, PKCS#8 unencrypted, PEM-encoded.</summary>
        public string PrivateKeyPkcs8Pem { get; init; } = string.Empty;

        /// <summary>Chain (intermediates), PEM-encoded, with the leaf EXCLUDED. May be empty.</summary>
        public string ChainPem { get; init; } = string.Empty;

        public string CommonName { get; init; } = string.Empty;
        public IReadOnlyList<string> SubjectAlternativeNames { get; init; } = Array.Empty<string>();

        /// <summary>"RSA" or "ECDSA".</summary>
        public string KeyAlgorithm { get; init; } = string.Empty;
        public int KeySize { get; init; }
    }

    /// <summary>
    /// Splits a Keyfactor-supplied PKCS#12/PFX into leaf certificate, PKCS#8 (unencrypted)
    /// private key, and chain WITHOUT the leaf — the exact shape Cloud Manager requires.
    /// Uses BouncyCastle so key export does not depend on the OS key store or Windows CNG
    /// export policy. Free of Keyfactor types so it is unit testable.
    /// </summary>
    public static class PfxSplitter
    {
        public static SplitCertificate Split(byte[] pfxBytes, string? password)
        {
            if (pfxBytes == null || pfxBytes.Length == 0)
                throw new ArgumentException("PFX content is empty.", nameof(pfxBytes));

            var store = new Pkcs12StoreBuilder().Build();
            using (var ms = new MemoryStream(pfxBytes))
            {
                store.Load(ms, (password ?? string.Empty).ToCharArray());
            }

            var keyAlias = store.Aliases.FirstOrDefault(store.IsKeyEntry)
                ?? throw new InvalidOperationException("PFX did not contain a private key entry.");

            var privateKey = store.GetKey(keyAlias).Key;
            var (algorithm, keySize) = DescribeKey(privateKey);

            var chainEntries = store.GetCertificateChain(keyAlias);
            X509Certificate leaf;
            var intermediates = new List<X509Certificate>();

            if (chainEntries != null && chainEntries.Length > 0)
            {
                leaf = chainEntries[0].Certificate;                       // leaf first
                intermediates.AddRange(chainEntries.Skip(1).Select(e => e.Certificate)); // leaf EXCLUDED
            }
            else
            {
                leaf = store.GetCertificate(keyAlias).Certificate;
            }

            var pkcs8Der = PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKey).GetDerEncoded();

            return new SplitCertificate
            {
                CertificatePem = ToPem("CERTIFICATE", leaf.GetEncoded()),
                PrivateKeyPkcs8Pem = ToPem("PRIVATE KEY", pkcs8Der),
                ChainPem = string.Join("\n", intermediates.Select(c => ToPem("CERTIFICATE", c.GetEncoded()))),
                CommonName = GetCommonName(leaf),
                SubjectAlternativeNames = GetDnsSans(leaf),
                KeyAlgorithm = algorithm,
                KeySize = keySize,
            };
        }

        private static (string algorithm, int keySize) DescribeKey(AsymmetricKeyParameter key) => key switch
        {
            RsaKeyParameters rsa => ("RSA", rsa.Modulus.BitLength),
            ECPrivateKeyParameters ec => ("ECDSA", ec.Parameters.N.BitLength),
            _ => throw new InvalidOperationException(
                "Leaf certificate uses an unsupported private key algorithm (expected RSA or ECDSA)."),
        };

        private static string GetCommonName(X509Certificate cert)
        {
            var values = cert.SubjectDN.GetValueList(X509Name.CN);
            return values.Count > 0 ? values[0]?.ToString() ?? string.Empty : string.Empty;
        }

        private static List<string> GetDnsSans(X509Certificate cert)
        {
            var sans = new List<string>();
            var altNames = cert.GetSubjectAlternativeNames();
            if (altNames == null) return sans;

            foreach (IList? entry in altNames)
            {
                // Each entry is [ int tagType, object value ]; dNSName == GeneralName.DnsName (2).
                if (entry is { Count: >= 2 } && entry[0] is int tag && tag == GeneralName.DnsName)
                {
                    var value = entry[1]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) sans.Add(value!);
                }
            }

            return sans;
        }

        private static string ToPem(string label, byte[] der) =>
            new string(System.Security.Cryptography.PemEncoding.Write(label, der));
    }
}
