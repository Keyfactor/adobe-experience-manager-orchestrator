using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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
    /// TODO (DESIGN.md §8): confirm the exact tenant-scoped programs listing operation and the
    /// required IMS scopes. This uses GET /api/programs and reads _embedded.programs[].id.
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

                var token = Auth!.GetAccessTokenAsync().GetAwaiter().GetResult();

                using var request = new HttpRequestMessage(
                    HttpMethod.Get, $"{Properties.BaseUrl.TrimEnd('/')}/api/programs");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.TryAddWithoutValidation("x-api-key", Properties.ClientId);
                if (!string.IsNullOrEmpty(Properties.ImsOrgId))
                    request.Headers.TryAddWithoutValidation("x-gw-ims-org-id", Properties.ImsOrgId);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = Http.SendAsync(request).GetAwaiter().GetResult();
                var payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    return Fail(config, $"Programs listing failed ({(int)response.StatusCode}): {payload}");

                var programIds = ParseProgramIds(payload);

                var accepted = submitDiscovery.Invoke(programIds);
                Logger.LogInformation("AEMCM Discovery found {Count} program(s); accepted={Accepted}",
                    programIds.Count, accepted);

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

        public static List<string> ParseProgramIds(string payload)
        {
            var ids = new List<string>();
            using var doc = JsonDocument.Parse(payload);

            if (doc.RootElement.TryGetProperty("_embedded", out var embedded)
                && embedded.TryGetProperty("programs", out var programs)
                && programs.ValueKind == JsonValueKind.Array)
            {
                foreach (var program in programs.EnumerateArray())
                {
                    if (!program.TryGetProperty("id", out var idElement)) continue;
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
