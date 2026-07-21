using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client.Models;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AEMCM
{
    /// <summary>Pulls all SSL certificates from a Cloud Manager program into Command.</summary>
    [Job(JobTypes.Inventory)]
    public class Inventory : AemcmJob<Inventory>, IInventoryJobExtension
    {
        public Inventory(IPAMSecretResolver resolver)
        {
            PamSecretResolver = resolver;
            Logger = LogHandler.GetClassLogger<Inventory>();
        }

        public JobResult ProcessJob(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventoryUpdate)
        {
            Logger.LogDebug("Begin AEMCM Inventory for store {StorePath}", config.CertificateStoreDetails?.StorePath);

            try
            {
                InitializeStore(config);

                // Report every cert (incl. Adobe-managed DV and expired) so the 70-cert budget is visible.
                var certs = Client!.GetAllCertificatesAsync().GetAwaiter().GetResult();
                var items = certs.Select(ToInventoryItem).ToList();

                var accepted = submitInventoryUpdate.Invoke(items);
                Logger.LogInformation("AEMCM Inventory submitted {Count} item(s); accepted={Accepted}", items.Count, accepted);

                return new JobResult
                {
                    JobHistoryId = config.JobHistoryId,
                    Result = accepted ? OrchestratorJobStatusJobResult.Success : OrchestratorJobStatusJobResult.Failure,
                    FailureMessage = accepted ? string.Empty : "Command rejected the submitted inventory.",
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "AEMCM Inventory failed");
                return new JobResult
                {
                    JobHistoryId = config.JobHistoryId,
                    Result = OrchestratorJobStatusJobResult.Failure,
                    FailureMessage = $"AEMCM Inventory failed: {ex.Message}",
                };
            }
        }

        private static CurrentInventoryItem ToInventoryItem(SslCertificateRepresentation cert)
        {
            var pems = new List<string>();
            if (!string.IsNullOrWhiteSpace(cert.Certificate)) pems.Add(cert.Certificate!);
            var hasChain = !string.IsNullOrWhiteSpace(cert.Chain);
            if (hasChain) pems.Add(cert.Chain!);

            return new CurrentInventoryItem
            {
                Alias = string.IsNullOrWhiteSpace(cert.Name)
                    ? cert.Id.ToString(CultureInfo.InvariantCulture)
                    : cert.Name!,
                Certificates = pems,
                PrivateKeyEntry = true,             // Adobe holds the key; it is never returned to us.
                UseChainLevel = hasChain,
                ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                Parameters = new Dictionary<string, object>
                {
                    ["CertificateId"] = cert.Id,
                    ["Type"] = cert.SslCertificateType ?? string.Empty,
                    ["Status"] = cert.SslCertificateStatus ?? string.Empty,
                    ["CommonName"] = cert.CommonName ?? string.Empty,
                    ["SubjectAlternativeNames"] = string.Join(",", cert.SubjectAlternativeNames),
                    ["ExpireAt"] = cert.ExpireAt,
                    ["AdobeManaged"] = cert.IsAdobeManaged,
                },
            };
        }
    }
}
