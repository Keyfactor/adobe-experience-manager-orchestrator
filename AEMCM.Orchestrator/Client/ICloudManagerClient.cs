
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client.Models;

namespace Keyfactor.Extensions.Orchestrator.AEMCM.Client
{
    /// <summary>
    /// Typed access to the Adobe Cloud Manager SSL Certificate and Domain Mapping endpoints,
    /// scoped to a single program. Kept behind an interface so job logic can be unit tested.
    /// </summary>
    public interface ICloudManagerClient
    {
        /// <summary>GET /api/program/{programId}/certificates — pages through all results.</summary>
        Task<IReadOnlyList<SslCertificateRepresentation>> GetAllCertificatesAsync(
            string? sslCertificateTypeFilter = null,
            string? statusFilter = null,
            CancellationToken cancellationToken = default);

        /// <summary>GET /api/program/{programId}/certificate/{id}.</summary>
        Task<SslCertificateRepresentation?> GetCertificateAsync(
            long certificateId, CancellationToken cancellationToken = default);

        /// <summary>POST /api/program/{programId}/certificates.</summary>
        Task<SslCertificateRepresentation> CreateCertificateAsync(
            CreateOrUpdateSslCertificateBody body, CancellationToken cancellationToken = default);

        /// <summary>PUT /api/program/{programId}/certificate/{id}.</summary>
        Task<SslCertificateRepresentation> UpdateCertificateAsync(
            long certificateId, CreateOrUpdateSslCertificateBody body,
            CancellationToken cancellationToken = default);

        /// <summary>DELETE /api/program/{programId}/certificate/{id}.</summary>
        Task DeleteCertificateAsync(long certificateId, CancellationToken cancellationToken = default);

        /// <summary>GET /api/program/{programId}/domain-mappings?certificateId={id}.</summary>
        Task<IReadOnlyList<DomainMapping>> GetDomainMappingsForCertificateAsync(
            long certificateId, CancellationToken cancellationToken = default);
    }
}
