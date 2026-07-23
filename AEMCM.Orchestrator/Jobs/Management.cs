
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Globalization;
using System.Linq;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client.Models;
using Keyfactor.Extensions.Orchestrator.AEMCM.Logic;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AEMCM
{
    /// <summary>
    /// Add (create/update, with SAN-consolidation and 70-cert budget handling) and Remove
    /// (with safe-delete guarded by domain mappings).
    /// </summary>
    [Job(JobTypes.Management)]
    public class Management : AemcmJob<Management>, IManagementJobExtension
    {
        // Behavior toggles — see DESIGN.md §8. Conservative defaults.
        private const bool AllowSupersetConsolidation = false; // don't silently re-issue onto a broader cert
        private const bool AllowExpiredReclaim = false;        // don't auto-delete expired certs to free budget

        public Management(IPAMSecretResolver resolver)
        {
            PamSecretResolver = resolver;
            Logger = LogHandler.GetClassLogger<Management>();
        }

        public JobResult ProcessJob(ManagementJobConfiguration config)
        {
            Logger.LogDebug("Begin AEMCM Management ({Op}) for store {StorePath}",
                config.OperationType, config.CertificateStoreDetails?.StorePath);

            try
            {
                InitializeStore(config);

                var jobCert = config.JobCertificate;
                return config.OperationType switch
                {
                    CertStoreOperationType.Add => PerformAddition(
                        jobCert?.Alias, jobCert?.Contents, jobCert?.PrivateKeyPassword,
                        config.Overwrite, config.JobHistoryId),
                    CertStoreOperationType.Remove => PerformRemoval(jobCert?.Alias, config.JobHistoryId),
                    _ => Fail(config.JobHistoryId, $"Unsupported operation '{config.OperationType}'."),
                };
            }
            catch (CloudManagerApiException apiEx)
            {
                // Message is already an operator-friendly summary of the Cloud Manager error.
                Logger.LogError(apiEx, "AEMCM Management: Cloud Manager API error");
                return Fail(config.JobHistoryId, apiEx.Message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "AEMCM Management failed");
                return Fail(config.JobHistoryId, $"AEMCM Management failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Add/renew a certificate: split the PFX, validate platform rules, then update an existing
        /// certificate (matched by alias or SAN set) or create a new one within the 70-cert budget.
        /// </summary>
        internal JobResult PerformAddition(
            string? alias, string? contents, string? pfxPassword, bool overwrite, long jobHistoryId)
        {
            if (string.IsNullOrWhiteSpace(contents))
                return Fail(jobHistoryId, "No certificate contents supplied for Add.");

            // 1. Split the PFX into leaf / PKCS#8 key / chain-without-leaf.
            SplitCertificate split;
            try
            {
                split = PfxSplitter.Split(Convert.FromBase64String(contents!), pfxPassword);
            }
            catch (Exception ex)
            {
                return Fail(jobHistoryId, $"Could not process the supplied certificate: {ex.Message}");
            }

            // 2. Validate against platform rules early.
            var validationError = ValidatePlatformRules(split);
            if (validationError != null) return Fail(jobHistoryId, validationError);

            var body = new CreateOrUpdateSslCertificateBody
            {
                Name = string.IsNullOrWhiteSpace(alias) ? split.CommonName : alias!,
                Certificate = split.CertificatePem,
                PrivateKey = new PrivateKeyValue { Value = split.PrivateKeyPkcs8Pem },
                Chain = split.ChainPem, // leaf already excluded by PfxSplitter
            };

            // Pre-send diagnostic summary. Never logs private key material — only shape/metadata,
            // so the first upload against a real cert is easy to diagnose (e.g. an empty chain).
            Logger.LogDebug(
                "Prepared certificate for upload: name={Name}, CN={CommonName}, SANs={SanCount}, key={KeyAlgorithm}-{KeySize}, chainCerts={ChainCount}.",
                body.Name, split.CommonName, split.SubjectAlternativeNames.Count,
                split.KeyAlgorithm, split.KeySize, CountChainCerts(split.ChainPem));

            // 3. Decide update vs add.
            var existing = Client!.GetAllCertificatesAsync().GetAwaiter().GetResult();

            // Guard: alias explicitly targeting an Adobe-managed (DV) cert.
            if (overwrite && !string.IsNullOrWhiteSpace(alias))
            {
                var aliasHit = existing.FirstOrDefault(c => CertMatcher.AliasMatches(c, alias!));
                if (aliasHit is { IsAdobeManaged: true })
                    return Fail(jobHistoryId, $"Alias '{alias}' resolves to an Adobe-managed (DV) certificate, which cannot be modified.");
            }

            // Enforce name uniqueness (alias = Adobe cert name). Without Overwrite, a duplicate name
            // is rejected so the alias stays a stable, round-tripping key.
            if (!overwrite
                && existing.Any(c => string.Equals(c.Name, body.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return Fail(jobHistoryId,
                    $"A certificate named '{body.Name}' already exists in this program. Enable Overwrite to update it.");
            }

            var match = CertMatcher.FindMatch(
                existing, split.SubjectAlternativeNames, alias, overwrite, AllowSupersetConsolidation);

            if (match.IsMatch)
            {
                if (!overwrite && match.MatchType != CertMatchType.Alias)
                {
                    return Fail(jobHistoryId,
                        $"An equivalent certificate (id {match.Certificate!.Id}) already exists. Enable Overwrite to update it.");
                }

                Logger.LogInformation("Updating existing certificate id {Id} (match={Match})",
                    match.Certificate!.Id, match.MatchType);
                var updated = Client.UpdateCertificateAsync(match.Certificate.Id, body).GetAwaiter().GetResult();
                return Success(jobHistoryId, $"Updated certificate id {updated.Id}.");
            }

            // 4. No match → create, budget permitting.
            if (BudgetManager.HasBudgetForNew(existing.Count))
            {
                var created = Client.CreateCertificateAsync(body).GetAwaiter().GetResult();
                return Success(jobHistoryId, $"Created certificate id {created.Id}.");
            }

            // 5. Budget exhausted → optional reclaim, else fail.
            if (AllowExpiredReclaim)
            {
                var reclaim = BudgetManager.PickReclaimCandidate(existing);
                if (reclaim != null)
                {
                    Logger.LogWarning("Budget full; reclaiming expired certificate id {Id}", reclaim.Id);
                    Client.DeleteCertificateAsync(reclaim.Id).GetAwaiter().GetResult();
                    var created = Client.CreateCertificateAsync(body).GetAwaiter().GetResult();
                    return Success(jobHistoryId, $"Reclaimed expired id {reclaim.Id}; created certificate id {created.Id}.");
                }
            }

            return Fail(jobHistoryId,
                $"Cannot add certificate: program is at the {BudgetManager.MaxCertificates}-certificate limit. " +
                "Delete expired or unused certificates and retry.");
        }

        /// <summary>Remove a certificate by alias, blocking deletion when it is in use by a domain mapping.</summary>
        internal JobResult PerformRemoval(string? alias, long jobHistoryId)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return Fail(jobHistoryId, "No alias supplied for Remove.");

            var existing = Client!.GetAllCertificatesAsync().GetAwaiter().GetResult();
            var matches = existing.Where(c => CertMatcher.AliasMatches(c, alias!)).ToList();
            if (matches.Count == 0)
            {
                Logger.LogInformation("Remove: no certificate matched alias '{Alias}'; treating as already removed.", alias);
                return Success(jobHistoryId, $"No certificate matched alias '{alias}'.");
            }

            if (matches.Count > 1)
            {
                var ids = string.Join(", ", matches.Select(m => m.Id.ToString(CultureInfo.InvariantCulture)));
                return Fail(jobHistoryId,
                    $"Alias '{alias}' matches multiple certificates (ids: {ids}). Remove by the disambiguated alias or resolve the duplicate names first.");
            }

            var target = matches[0];

            if (target.IsAdobeManaged)
                return Fail(jobHistoryId, $"Certificate '{alias}' is Adobe-managed (DV) and cannot be removed by this extension.");

            // Safe-delete: block if any domain mapping references the cert.
            var mappings = Client.GetDomainMappingsForCertificateAsync(target.Id).GetAwaiter().GetResult();
            if (mappings.Count > 0)
            {
                var names = string.Join(", ", mappings.Select(m => m.DomainName ?? m.DomainMappingId.ToString(CultureInfo.InvariantCulture)));
                return Fail(jobHistoryId,
                    $"Certificate id {target.Id} is in use by domain mapping(s): {names}. Remove the mapping(s) before deleting.");
            }

            Client.DeleteCertificateAsync(target.Id).GetAwaiter().GetResult();
            return Success(jobHistoryId, $"Deleted certificate id {target.Id}. Run the pipeline to fully undeploy.");
        }

        private static int CountChainCerts(string? chainPem) =>
            string.IsNullOrEmpty(chainPem)
                ? 0
                : chainPem.Split("-----BEGIN CERTIFICATE-----").Length - 1;

        private static string? ValidatePlatformRules(SplitCertificate split)
        {
            if (!BudgetManager.IsWithinSanLimit(split.SubjectAlternativeNames.Count))
                return $"Certificate has {split.SubjectAlternativeNames.Count} SANs; the maximum is {BudgetManager.MaxSubjectAlternativeNames}.";

            if (split.KeyAlgorithm == "RSA")
            {
                return split.KeySize == 2048
                    ? null
                    : $"RSA key size {split.KeySize} is not supported (only RSA-2048). Use ECDSA (secp256r1/secp384r1) for stronger keys.";
            }

            if (split.KeyAlgorithm == "ECDSA")
            {
                return split.KeySize is 256 or 384
                    ? null
                    : $"EC key size {split.KeySize} is not supported (only secp256r1/secp384r1).";
            }

            return $"Unsupported key algorithm '{split.KeyAlgorithm}'.";
        }

        private JobResult Success(long jobHistoryId, string message)
        {
            Logger.LogInformation("{Message}", message);
            return new JobResult
            {
                JobHistoryId = jobHistoryId,
                Result = OrchestratorJobStatusJobResult.Success,
                FailureMessage = string.Empty,
            };
        }

        private JobResult Fail(long jobHistoryId, string message)
        {
            Logger.LogError("{Message}", message);
            return new JobResult
            {
                JobHistoryId = jobHistoryId,
                Result = OrchestratorJobStatusJobResult.Failure,
                FailureMessage = message,
            };
        }
    }
}
