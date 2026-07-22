
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

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
