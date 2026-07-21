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
