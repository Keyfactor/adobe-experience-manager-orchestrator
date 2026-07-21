using System.Collections.Generic;
using System.Linq;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client.Models;

namespace Keyfactor.Extensions.Orchestrator.AEMCM.Logic
{
    /// <summary>
    /// Enforces the Cloud Manager per-program certificate budget (70 total, including Adobe-managed
    /// DV and expired certs) and the 100-SAN-per-cert limit, and picks a reclaim candidate when full.
    /// </summary>
    public static class BudgetManager
    {
        /// <summary>Maximum installed certificates per program (customer + DV + expired).</summary>
        public const int MaxCertificates = 70;

        /// <summary>Maximum SANs per certificate.</summary>
        public const int MaxSubjectAlternativeNames = 100;

        /// <summary>True when a new certificate can be created without exceeding the budget.</summary>
        public static bool HasBudgetForNew(int totalCertificateCount) =>
            totalCertificateCount < MaxCertificates;

        /// <summary>True when the incoming SAN count is within the per-cert limit.</summary>
        public static bool IsWithinSanLimit(int sanCount) =>
            sanCount <= MaxSubjectAlternativeNames;

        /// <summary>
        /// Chooses a certificate to delete to free budget: the oldest EXPIRED customer-managed cert.
        /// Returns null when nothing is safe to reclaim. Adobe-managed (DV) certs are never reclaimed.
        /// </summary>
        public static SslCertificateRepresentation? PickReclaimCandidate(
            IEnumerable<SslCertificateRepresentation> existing) =>
            existing
                .Where(c => c.IsExpired && !c.IsAdobeManaged)
                .OrderBy(c => c.ExpireAt)
                .ThenBy(c => c.CreatedAt)
                .FirstOrDefault();
    }
}
