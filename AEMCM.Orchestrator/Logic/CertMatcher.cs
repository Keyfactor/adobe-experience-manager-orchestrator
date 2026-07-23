
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

namespace Keyfactor.Extensions.Orchestrator.AEMCM.Logic
{
    public enum CertMatchType
    {
        /// <summary>No existing certificate matched; a new one must be created.</summary>
        None = 0,

        /// <summary>Explicit alias match (Overwrite=true resolved to an existing cert).</summary>
        Alias = 1,

        /// <summary>Existing cert covers exactly the same SAN set — a renewal.</summary>
        ExactSanSet = 2,

        /// <summary>Incoming SAN set is a superset of an existing cert — a safe expansion.</summary>
        SanSuperset = 3,
    }

    public sealed class CertMatchResult
    {
        public CertMatchType MatchType { get; init; }
        public SslCertificateRepresentation? Certificate { get; init; }

        public bool IsMatch => MatchType != CertMatchType.None && Certificate != null;

        public static readonly CertMatchResult NoMatch = new() { MatchType = CertMatchType.None };
    }

    /// <summary>
    /// Decides whether an incoming certificate should update an existing Cloud Manager cert
    /// (to conserve the 70-cert budget) or be created new. Adobe-managed (DV) certs are never
    /// considered writable matches.
    /// </summary>
    public static class CertMatcher
    {
        /// <param name="existing">Existing certs in the program (any type; DV filtered out internally).</param>
        /// <param name="incomingSans">SAN set of the incoming certificate.</param>
        /// <param name="alias">Keyfactor alias for the entry (may map to a cert name or id).</param>
        /// <param name="overwrite">Whether the Command job requested overwrite.</param>
        /// <param name="allowSuperset">
        /// When true, an incoming SAN set that is a strict superset of an existing customer-managed
        /// cert is treated as a match (consolidation/expansion). Off by default.
        /// </param>
        public static CertMatchResult FindMatch(
            IEnumerable<SslCertificateRepresentation> existing,
            IEnumerable<string> incomingSans,
            string? alias,
            bool overwrite,
            bool allowSuperset = false)
        {
            var writable = existing.Where(c => !c.IsAdobeManaged).ToList();
            var incoming = Normalize(incomingSans);

            // 1. Explicit alias match wins when overwrite was requested.
            if (overwrite && !string.IsNullOrWhiteSpace(alias))
            {
                var byAlias = writable.FirstOrDefault(c => AliasMatches(c, alias!));
                if (byAlias != null)
                    return new CertMatchResult { MatchType = CertMatchType.Alias, Certificate = byAlias };
            }

            // 2. Identical SAN set → renewal of the same logical certificate.
            var exact = writable.FirstOrDefault(c => Normalize(c.SubjectAlternativeNames).SetEquals(incoming));
            if (exact != null)
                return new CertMatchResult { MatchType = CertMatchType.ExactSanSet, Certificate = exact };

            // 3. Optional: incoming ⊇ existing → safe expansion onto one cert.
            if (allowSuperset && incoming.Count > 0)
            {
                var superset = writable
                    .Where(c => Normalize(c.SubjectAlternativeNames) is { Count: > 0 } s && incoming.IsSupersetOf(s))
                    .OrderByDescending(c => Normalize(c.SubjectAlternativeNames).Count)
                    .FirstOrDefault();
                if (superset != null)
                    return new CertMatchResult { MatchType = CertMatchType.SanSuperset, Certificate = superset };
            }

            return CertMatchResult.NoMatch;
        }

        /// <summary>
        /// Matches a Keyfactor alias to a certificate by name, by numeric id, or by the
        /// disambiguated inventory form "name (id)" used for pre-existing duplicate names.
        /// </summary>
        public static bool AliasMatches(SslCertificateRepresentation cert, string alias)
        {
            if (string.IsNullOrWhiteSpace(alias)) return false;

            var id = cert.Id.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(id, alias, StringComparison.Ordinal)) return true;
            if (string.Equals(cert.Name, alias, StringComparison.OrdinalIgnoreCase)) return true;

            return !string.IsNullOrWhiteSpace(cert.Name)
                && string.Equals($"{cert.Name} ({id})", alias, StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> Normalize(IEnumerable<string> sans) =>
            new(sans.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLowerInvariant()),
                StringComparer.Ordinal);
    }
}
