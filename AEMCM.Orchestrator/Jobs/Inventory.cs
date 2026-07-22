
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

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

                // Alias = Adobe certificate name. KF-managed names are kept unique on Add, but a
                // program may already contain externally-created duplicates; disambiguate those with
                // the id so Command never receives colliding aliases.
                var duplicateNames = certs
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                    .GroupBy(c => c.Name!, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (duplicateNames.Count > 0)
                    Logger.LogWarning("Found {Count} duplicate certificate name(s); those aliases are disambiguated with the certificate id.", duplicateNames.Count);

                var items = certs.Select(c => ToInventoryItem(c, duplicateNames)).ToList();

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

        private static CurrentInventoryItem ToInventoryItem(
            SslCertificateRepresentation cert, HashSet<string> duplicateNames)
        {
            var pems = new List<string>();
            if (!string.IsNullOrWhiteSpace(cert.Certificate)) pems.Add(cert.Certificate!);
            var hasChain = !string.IsNullOrWhiteSpace(cert.Chain);
            if (hasChain) pems.Add(cert.Chain!);

            return new CurrentInventoryItem
            {
                // Alias = Adobe certificate name (round-trips with enrollment). Fall back to the id
                // when the name is empty, and disambiguate pre-existing duplicate names with the id.
                Alias = ResolveAlias(cert, duplicateNames),
                Certificates = pems,
                PrivateKeyEntry = true,             // Adobe holds the key; it is never returned to us.
                UseChainLevel = hasChain,
                ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                Parameters = new Dictionary<string, object>
                {
                    ["CertificateId"] = cert.Id,
                    ["Name"] = cert.Name ?? string.Empty,
                    ["Type"] = cert.SslCertificateType ?? string.Empty,
                    ["Status"] = cert.SslCertificateStatus ?? string.Empty,
                    ["CommonName"] = cert.CommonName ?? string.Empty,
                    ["SubjectAlternativeNames"] = string.Join(",", cert.SubjectAlternativeNames),
                    ["ExpireAt"] = cert.ExpireAt,
                    ["AdobeManaged"] = cert.IsAdobeManaged,
                },
            };
        }

        private static string ResolveAlias(SslCertificateRepresentation cert, HashSet<string> duplicateNames)
        {
            if (string.IsNullOrWhiteSpace(cert.Name))
                return cert.Id.ToString(CultureInfo.InvariantCulture);

            return duplicateNames.Contains(cert.Name!)
                ? $"{cert.Name} ({cert.Id.ToString(CultureInfo.InvariantCulture)})"
                : cert.Name!;
        }
    }
}
