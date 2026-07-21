using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keyfactor.Extensions.Orchestrator.AEMCM
{
    /// <summary>Shared System.Text.Json options for the extension.</summary>
    public static class AemcmJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public static class AemcmConstants
    {
        /// <summary>Certificate Store Type short name (must match the manifest capability and integration-manifest).</summary>
        public const string StoreTypeName = "AEMCM";

        public const string DefaultBaseUrl = "https://cloudmanager.adobe.io";
        public const string DefaultImsTokenUrl = "https://ims-na1.adobelogin.com/ims/token/v3";
        public const string DefaultImsScopes =
            "openid,AdobeID,read_organizations,additional_info.projectedProductContext";
    }
}
