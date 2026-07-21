using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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
    /// Depends only on the BCL; deliberately free of any Keyfactor types so it is unit testable.
    /// </summary>
    public static class PfxSplitter
    {
        private const string SubjectAltNameOid = "2.5.29.17";

        public static SplitCertificate Split(byte[] pfxBytes, string? password)
        {
            if (pfxBytes == null || pfxBytes.Length == 0)
                throw new ArgumentException("PFX content is empty.", nameof(pfxBytes));

#if NET9_0_OR_GREATER
            var collection = X509CertificateLoader.LoadPkcs12Collection(
                pfxBytes, password, X509KeyStorageFlags.Exportable);
#else
            var collection = new X509Certificate2Collection();
            collection.Import(pfxBytes, password, X509KeyStorageFlags.Exportable);
#endif

            X509Certificate2? leaf = FindLeaf(collection);
            if (leaf == null)
                throw new InvalidOperationException("PFX did not contain a certificate with a private key.");

            var (algorithm, keySize, pkcs8) = ExportPrivateKey(leaf);

            var chainCerts = collection
                .Cast<X509Certificate2>()
                .Where(c => !c.RawData.SequenceEqual(leaf.RawData))
                .OrderBy(IsRootCertificate) // intermediates before any root
                .ToList();

            var chainPem = string.Join(
                "\n",
                chainCerts.Select(c => ToPem("CERTIFICATE", c.RawData)));

            return new SplitCertificate
            {
                CertificatePem = ToPem("CERTIFICATE", leaf.RawData),
                PrivateKeyPkcs8Pem = ToPem("PRIVATE KEY", pkcs8),
                ChainPem = chainPem,
                CommonName = leaf.GetNameInfo(X509NameType.SimpleName, forIssuer: false) ?? string.Empty,
                SubjectAlternativeNames = ReadDnsSans(leaf),
                KeyAlgorithm = algorithm,
                KeySize = keySize,
            };
        }

        private static X509Certificate2? FindLeaf(X509Certificate2Collection collection)
        {
            // Prefer an end-entity (non-CA) cert that has the private key.
            var withKey = collection.Cast<X509Certificate2>().Where(c => c.HasPrivateKey).ToList();
            if (withKey.Count == 0) return null;
            return withKey.FirstOrDefault(c => !IsCaCertificate(c)) ?? withKey[0];
        }

        private static (string algorithm, int keySize, byte[] pkcs8) ExportPrivateKey(X509Certificate2 leaf)
        {
            using var rsa = leaf.GetRSAPrivateKey();
            if (rsa != null)
                return ("RSA", rsa.KeySize, rsa.ExportPkcs8PrivateKey());

            using var ecdsa = leaf.GetECDsaPrivateKey();
            if (ecdsa != null)
                return ("ECDSA", ecdsa.KeySize, ecdsa.ExportPkcs8PrivateKey());

            throw new InvalidOperationException(
                "Leaf certificate uses an unsupported private key algorithm (expected RSA or ECDSA).");
        }

        private static bool IsCaCertificate(X509Certificate2 cert)
        {
            foreach (var ext in cert.Extensions)
            {
                if (ext is X509BasicConstraintsExtension bc)
                    return bc.CertificateAuthority;
            }
            return false;
        }

        // Sort key: root certs (self-issued) come last in the chain output.
        private static bool IsRootCertificate(X509Certificate2 cert) =>
            string.Equals(cert.SubjectName.RawData is { } s ? Convert.ToBase64String(s) : null,
                          cert.IssuerName.RawData is { } i ? Convert.ToBase64String(i) : null,
                          StringComparison.Ordinal);

        /// <summary>Parses dNSName entries from the SAN extension (DER) without locale-sensitive string parsing.</summary>
        private static List<string> ReadDnsSans(X509Certificate2 cert)
        {
            var sans = new List<string>();
            var ext = cert.Extensions[SubjectAltNameOid];
            if (ext == null) return sans;

            try
            {
                var reader = new AsnReader(ext.RawData, AsnEncodingRules.DER);
                var sequence = reader.ReadSequence();
                while (sequence.HasData)
                {
                    var tag = sequence.PeekTag();
                    // GeneralName CHOICE: dNSName is context-specific [2], IA5String.
                    if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 2)
                    {
                        var dns = sequence.ReadCharacterString(
                            UniversalTagNumber.IA5String,
                            new Asn1Tag(TagClass.ContextSpecific, 2));
                        if (!string.IsNullOrWhiteSpace(dns)) sans.Add(dns);
                    }
                    else
                    {
                        sequence.ReadEncodedValue(); // skip other GeneralName choices
                    }
                }
            }
            catch (AsnContentException)
            {
                // Malformed SAN extension — return whatever we parsed rather than failing the whole job.
            }

            return sans;
        }

        private static string ToPem(string label, byte[] der) =>
            new string(PemEncoding.Write(label, der));
    }
}
