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

                return config.OperationType switch
                {
                    CertStoreOperationType.Add => HandleAdd(config),
                    CertStoreOperationType.Remove => HandleRemove(config),
                    _ => Fail(config, $"Unsupported operation '{config.OperationType}'."),
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "AEMCM Management failed");
                return Fail(config, $"AEMCM Management failed: {ex.Message}");
            }
        }

        private JobResult HandleAdd(ManagementJobConfiguration config)
        {
            var jobCert = config.JobCertificate;
            if (string.IsNullOrWhiteSpace(jobCert?.Contents))
                return Fail(config, "No certificate contents supplied for Add.");

            // 1. Split the PFX into leaf / PKCS#8 key / chain-without-leaf.
            var pfxBytes = Convert.FromBase64String(jobCert!.Contents);
            SplitCertificate split;
            try
            {
                split = PfxSplitter.Split(pfxBytes, jobCert.PrivateKeyPassword);
            }
            catch (Exception ex)
            {
                return Fail(config, $"Could not process the supplied certificate: {ex.Message}");
            }

            // 2. Validate against platform rules early.
            var validationError = ValidatePlatformRules(split);
            if (validationError != null) return Fail(config, validationError);

            var body = new CreateOrUpdateSslCertificateBody
            {
                Name = string.IsNullOrWhiteSpace(jobCert.Alias) ? split.CommonName : jobCert.Alias!,
                Certificate = split.CertificatePem,
                PrivateKey = new PrivateKeyValue { Value = split.PrivateKeyPkcs8Pem },
                Chain = split.ChainPem, // leaf already excluded by PfxSplitter
            };

            // 3. Decide update vs add.
            var existing = Client!.GetAllCertificatesAsync().GetAwaiter().GetResult();

            // Guard: alias explicitly targeting an Adobe-managed (DV) cert.
            if (config.Overwrite && !string.IsNullOrWhiteSpace(jobCert.Alias))
            {
                var aliasHit = existing.FirstOrDefault(c => AliasMatches(c, jobCert.Alias!));
                if (aliasHit is { IsAdobeManaged: true })
                    return Fail(config, $"Alias '{jobCert.Alias}' resolves to an Adobe-managed (DV) certificate, which cannot be modified.");
            }

            var match = CertMatcher.FindMatch(
                existing, split.SubjectAlternativeNames, jobCert.Alias, config.Overwrite, AllowSupersetConsolidation);

            if (match.IsMatch)
            {
                if (!config.Overwrite && match.MatchType != CertMatchType.Alias)
                {
                    return Fail(config,
                        $"An equivalent certificate (id {match.Certificate!.Id}) already exists. Enable Overwrite to update it.");
                }

                Logger.LogInformation("Updating existing certificate id {Id} (match={Match})",
                    match.Certificate!.Id, match.MatchType);
                var updated = Client.UpdateCertificateAsync(match.Certificate.Id, body).GetAwaiter().GetResult();
                return Success(config, $"Updated certificate id {updated.Id}.");
            }

            // 4. No match → create, budget permitting.
            if (BudgetManager.HasBudgetForNew(existing.Count))
            {
                var created = Client.CreateCertificateAsync(body).GetAwaiter().GetResult();
                return Success(config, $"Created certificate id {created.Id}.");
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
                    return Success(config, $"Reclaimed expired id {reclaim.Id}; created certificate id {created.Id}.");
                }
            }

            return Fail(config,
                $"Cannot add certificate: program is at the {BudgetManager.MaxCertificates}-certificate limit. " +
                "Delete expired or unused certificates and retry.");
        }

        private JobResult HandleRemove(ManagementJobConfiguration config)
        {
            var alias = config.JobCertificate?.Alias;
            if (string.IsNullOrWhiteSpace(alias))
                return Fail(config, "No alias supplied for Remove.");

            var existing = Client!.GetAllCertificatesAsync().GetAwaiter().GetResult();
            var target = existing.FirstOrDefault(c => AliasMatches(c, alias!));
            if (target == null)
            {
                Logger.LogInformation("Remove: no certificate matched alias '{Alias}'; treating as already removed.", alias);
                return Success(config, $"No certificate matched alias '{alias}'.");
            }

            if (target.IsAdobeManaged)
                return Fail(config, $"Certificate '{alias}' is Adobe-managed (DV) and cannot be removed by this extension.");

            // Safe-delete: block if any domain mapping references the cert.
            var mappings = Client.GetDomainMappingsForCertificateAsync(target.Id).GetAwaiter().GetResult();
            if (mappings.Count > 0)
            {
                var names = string.Join(", ", mappings.Select(m => m.DomainName ?? m.DomainMappingId.ToString(CultureInfo.InvariantCulture)));
                return Fail(config,
                    $"Certificate id {target.Id} is in use by domain mapping(s): {names}. Remove the mapping(s) before deleting.");
            }

            Client.DeleteCertificateAsync(target.Id).GetAwaiter().GetResult();
            return Success(config, $"Deleted certificate id {target.Id}. Run the pipeline to fully undeploy.");
        }

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

        private static bool AliasMatches(SslCertificateRepresentation cert, string alias) =>
            string.Equals(cert.Name, alias, StringComparison.OrdinalIgnoreCase)
            || string.Equals(cert.Id.ToString(CultureInfo.InvariantCulture), alias, StringComparison.Ordinal);

        private JobResult Success(ManagementJobConfiguration config, string message)
        {
            Logger.LogInformation("{Message}", message);
            return new JobResult
            {
                JobHistoryId = config.JobHistoryId,
                Result = OrchestratorJobStatusJobResult.Success,
                FailureMessage = string.Empty,
            };
        }

        private JobResult Fail(ManagementJobConfiguration config, string message)
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
