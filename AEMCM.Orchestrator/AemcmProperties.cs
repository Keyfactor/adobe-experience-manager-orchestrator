using System.Text.Json.Serialization;

namespace Keyfactor.Extensions.Orchestrator.AEMCM
{
    /// <summary>
    /// Resolved connection/config values for a job, assembled by <c>AemcmJob.InitializeStore</c>
    /// from the store credentials, store path, and custom fields.
    /// </summary>
    public class AemcmProperties
    {
        /// <summary>IMS Client ID (API key), from Server Username.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>IMS Client Secret, from Server Password.</summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>IMS Org ID → x-gw-ims-org-id header.</summary>
        public string ImsOrgId { get; set; } = string.Empty;

        /// <summary>Cloud Manager base URL, from the store Client Machine.</summary>
        public string BaseUrl { get; set; } = AemcmConstants.DefaultBaseUrl;

        public string ImsTokenUrl { get; set; } = AemcmConstants.DefaultImsTokenUrl;
        public string ImsScopes { get; set; } = AemcmConstants.DefaultImsScopes;

        /// <summary>Raw store path (numeric Cloud Manager programId).</summary>
        public string? StorePath { get; set; }

        /// <summary>Parsed programId; 0 for jobs without a store (e.g. Discovery).</summary>
        public long ProgramId { get; set; }
    }

    /// <summary>Store-type Custom Fields, deserialized from CertificateStoreDetails.Properties (a JSON string).</summary>
    public class StoreCustomFields
    {
        [JsonPropertyName("ImsOrgId")] public string? ImsOrgId { get; set; }
        [JsonPropertyName("ImsTokenUrl")] public string? ImsTokenUrl { get; set; }
        [JsonPropertyName("ImsScopes")] public string? ImsScopes { get; set; }
    }
}
