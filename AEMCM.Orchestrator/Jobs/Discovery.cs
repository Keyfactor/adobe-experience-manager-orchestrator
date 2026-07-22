
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AEMCM
{
    /// <summary>
    /// Enumerates the Cloud Manager programs the credentials can access and returns each
    /// programId as a discoverable store path.
    /// </summary>
    /// <remarks>
    /// A Discovery job has no store, so it cannot read the store's custom fields. Supply one or
    /// more comma-separated <b>IMS Org IDs</b> in the discovery <b>Directories to search</b> field.
    /// For each org, this lists tenants (GET /api/tenants) and then that tenant's programs
    /// (GET /api/tenant/{tenantId}/programs), aggregating the program IDs. The deprecated
    /// GET /api/programs is intentionally not used.
    /// </remarks>
    [Job(JobTypes.Discovery)]
    public class Discovery : AemcmJob<Discovery>, IDiscoveryJobExtension
    {
        public Discovery(IPAMSecretResolver resolver)
        {
            PamSecretResolver = resolver;
            Logger = LogHandler.GetClassLogger<Discovery>();
        }

        public JobResult ProcessJob(DiscoveryJobConfiguration config, SubmitDiscoveryUpdate submitDiscovery)
        {
            Logger.LogDebug("Begin AEMCM Discovery on {ClientMachine}", config.ClientMachine);

            try
            {
                InitializeStore(config);

                var orgIds = GetOrgIds(config);
                if (orgIds.Count == 0)
                {
                    return Fail(config,
                        "No IMS Org ID provided. Enter one or more comma-separated IMS Org IDs in the " +
                        "'Directories to search' field of the discovery schedule.");
                }

                var token = Auth!.GetAccessTokenAsync().GetAwaiter().GetResult();
                var baseUrl = Properties.BaseUrl.TrimEnd('/');
                var programIds = new HashSet<string>(StringComparer.Ordinal);

                foreach (var orgId in orgIds)
                {
                    var tenantsPayload = SendGet($"{baseUrl}/api/tenants", token, orgId);
                    foreach (var tenantId in ParseTenantIds(tenantsPayload))
                    {
                        var programsPayload = SendGet(
                            $"{baseUrl}/api/tenant/{Uri.EscapeDataString(tenantId)}/programs", token, orgId);
                        foreach (var programId in ParseProgramIds(programsPayload))
                            programIds.Add(programId);
                    }
                }

                var discovered = programIds.ToList();
                var accepted = submitDiscovery.Invoke(discovered);
                Logger.LogInformation("AEMCM Discovery found {Count} program(s); accepted={Accepted}",
                    discovered.Count, accepted);

                return new JobResult
                {
                    JobHistoryId = config.JobHistoryId,
                    Result = accepted ? OrchestratorJobStatusJobResult.Success : OrchestratorJobStatusJobResult.Failure,
                    FailureMessage = accepted ? string.Empty : "Command rejected the discovery results.",
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "AEMCM Discovery failed");
                return Fail(config, $"AEMCM Discovery failed: {ex.Message}");
            }
        }

        private string SendGet(string url, string token, string orgId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("x-api-key", Properties.ClientId);
            request.Headers.TryAddWithoutValidation("x-gw-ims-org-id", orgId);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = Http.SendAsync(request).GetAwaiter().GetResult();
            var payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new CloudManagerApiException((int)response.StatusCode, $"GET {url} failed ({(int)response.StatusCode}): {payload}");
            return payload;
        }

        private static List<string> GetOrgIds(DiscoveryJobConfiguration config)
        {
            var result = new List<string>();
            if (config.JobProperties != null
                && config.JobProperties.TryGetValue("dirs", out var value)
                && value?.ToString() is { } raw
                && !string.IsNullOrWhiteSpace(raw))
            {
                result.AddRange(raw.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
            }
            return result;
        }

        public static List<string> ParseProgramIds(string payload) => ParseEmbeddedIds(payload, "programs");

        public static List<string> ParseTenantIds(string payload) => ParseEmbeddedIds(payload, "tenants");

        private static List<string> ParseEmbeddedIds(string payload, string collectionName)
        {
            var ids = new List<string>();
            using var doc = JsonDocument.Parse(payload);

            if (doc.RootElement.TryGetProperty("_embedded", out var embedded)
                && embedded.TryGetProperty(collectionName, out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idElement)) continue;
                    var id = idElement.ValueKind == JsonValueKind.String
                        ? idElement.GetString()
                        : idElement.GetRawText();
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id!);
                }
            }

            return ids;
        }

        private JobResult Fail(DiscoveryJobConfiguration config, string message)
        {
            Logger.LogError("{Message}", message);
            return new JobResult
            {
                JobHistoryId = config.JobHistoryId,
                Result = OrchestratorJobStatusJobResult.Failure,
                FailureMessage = message,
            };
        }
    }
}
