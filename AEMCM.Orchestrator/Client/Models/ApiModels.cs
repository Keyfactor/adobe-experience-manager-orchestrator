using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Keyfactor.Extensions.Orchestrator.AEMCM.Client.Models
{
    /// <summary>Certificate origin type. DV = Adobe-managed (read-only to this extension).</summary>
    public static class SslCertificateType
    {
        public const string Dv = "DV";
        public const string Ev = "EV";
        public const string Ov = "OV";
    }

    public static class SslCertificateStatus
    {
        public const string Pending = "PENDING";
        public const string Valid = "VALID";
        public const string Expired = "EXPIRED";
    }

    /// <summary>
    /// Cloud Manager <c>SslCertificateRepresentation</c> — the item type returned by
    /// GET /certificates and GET /certificate/{id}. No private key is ever returned.
    /// </summary>
    public class SslCertificateRepresentation
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("programId")] public long ProgramId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("sslCertificateType")] public string? SslCertificateType { get; set; }
        [JsonPropertyName("sslCertificateStatus")] public string? SslCertificateStatus { get; set; }
        [JsonPropertyName("serialNumber")] public string? SerialNumber { get; set; }
        [JsonPropertyName("issuer")] public string? Issuer { get; set; }
        [JsonPropertyName("commonName")] public string? CommonName { get; set; }
        [JsonPropertyName("subjectAlternativeNames")] public List<string> SubjectAlternativeNames { get; set; } = new();
        [JsonPropertyName("certificate")] public string? Certificate { get; set; }
        [JsonPropertyName("chain")] public string? Chain { get; set; }
        [JsonPropertyName("expireAt")] public long ExpireAt { get; set; }
        [JsonPropertyName("createdAt")] public long CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")] public long UpdatedAt { get; set; }

        [JsonIgnore]
        public bool IsAdobeManaged =>
            string.Equals(SslCertificateType, Models.SslCertificateType.Dv, StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsExpired =>
            string.Equals(SslCertificateStatus, Models.SslCertificateStatus.Expired, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Nested private-key wrapper used by <see cref="CreateOrUpdateSslCertificateBody"/>.</summary>
    public class PrivateKeyValue
    {
        [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Cloud Manager <c>CreateOrUpdateSslCertificateBody</c>. Required: name, certificate,
    /// privateKey, chain. NOTE: <see cref="PrivateKey"/> is a nested object, and the leaf
    /// certificate MUST NOT appear in <see cref="Chain"/>.
    /// </summary>
    public class CreateOrUpdateSslCertificateBody
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("certificate")] public string Certificate { get; set; } = string.Empty;
        [JsonPropertyName("privateKey")] public PrivateKeyValue PrivateKey { get; set; } = new();
        [JsonPropertyName("chain")] public string Chain { get; set; } = string.Empty;
    }

    /// <summary>HAL link.</summary>
    public class HalLink
    {
        [JsonPropertyName("href")] public string? Href { get; set; }
    }

    /// <summary>Paginated list wrapper for GET /certificates (<c>AllSSLCertificatesList</c>).</summary>
    public class AllSslCertificatesList
    {
        [JsonPropertyName("_totalNumberOfItems")] public int TotalNumberOfItems { get; set; }
        [JsonPropertyName("_embedded")] public EmbeddedCertificates Embedded { get; set; } = new();
        [JsonPropertyName("_links")] public ListLinks Links { get; set; } = new();

        public class EmbeddedCertificates
        {
            [JsonPropertyName("certificates")]
            public List<SslCertificateRepresentation> Certificates { get; set; } = new();
        }

        public class ListLinks
        {
            [JsonPropertyName("next")] public HalLink? Next { get; set; }
            [JsonPropertyName("self")] public HalLink? Self { get; set; }
        }
    }

    /// <summary>Cloud Manager <c>DomainMapping</c> — links a domain to a certificate.</summary>
    public class DomainMapping
    {
        [JsonPropertyName("domainMappingId")] public long DomainMappingId { get; set; }
        [JsonPropertyName("programId")] public long ProgramId { get; set; }
        [JsonPropertyName("domainName")] public string? DomainName { get; set; }
        [JsonPropertyName("domainMappingStatus")] public string? DomainMappingStatus { get; set; }
        [JsonPropertyName("certificateId")] public long CertificateId { get; set; }
        [JsonPropertyName("domainId")] public long DomainId { get; set; }
        [JsonPropertyName("tier")] public string? Tier { get; set; }
    }

    /// <summary>Cloud Manager <c>DomainMappingList</c>.</summary>
    public class DomainMappingList
    {
        [JsonPropertyName("totalNumberOfItems")] public int TotalNumberOfItems { get; set; }
        [JsonPropertyName("domainMappings")] public List<DomainMapping> DomainMappings { get; set; } = new();
    }

    /// <summary>Adobe IMS OAuth Server-to-Server token response.</summary>
    public class ImsTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }

        /// <summary>Token lifetime in <b>seconds</b> (OAuth Server-to-Server; typically 86399 ≈ 24h).</summary>
        [JsonPropertyName("expires_in")] public long ExpiresInSeconds { get; set; }
    }
}
