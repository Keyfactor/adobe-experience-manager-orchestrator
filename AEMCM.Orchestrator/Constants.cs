
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

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
