
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client.Models;

namespace AEMCM.Orchestrator.Tests.TestHelpers
{
    /// <summary>
    /// In-memory <see cref="ICloudManagerClient"/> for exercising the Management job flows.
    /// Configure <see cref="Existing"/> and <see cref="DomainMappings"/>; inspect the recorded
    /// <see cref="Created"/>, <see cref="Updated"/>, and <see cref="Deleted"/> lists after a call.
    /// </summary>
    public sealed class FakeCloudManagerClient : ICloudManagerClient
    {
        public List<SslCertificateRepresentation> Existing { get; } = new();
        public Dictionary<long, List<DomainMapping>> DomainMappings { get; } = new();

        public List<CreateOrUpdateSslCertificateBody> Created { get; } = new();
        public List<(long Id, CreateOrUpdateSslCertificateBody Body)> Updated { get; } = new();
        public List<long> Deleted { get; } = new();

        public long NextCreatedId { get; set; } = 1000;

        public Task<IReadOnlyList<SslCertificateRepresentation>> GetAllCertificatesAsync(
            string? sslCertificateTypeFilter = null, string? statusFilter = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SslCertificateRepresentation>>(Existing.ToList());

        public Task<SslCertificateRepresentation?> GetCertificateAsync(
            long certificateId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Existing.FirstOrDefault(c => c.Id == certificateId));

        public Task<SslCertificateRepresentation> CreateCertificateAsync(
            CreateOrUpdateSslCertificateBody body, CancellationToken cancellationToken = default)
        {
            Created.Add(body);
            var created = new SslCertificateRepresentation
            {
                Id = NextCreatedId++,
                Name = body.Name,
                SslCertificateType = SslCertificateType.Ov,
                SslCertificateStatus = SslCertificateStatus.Valid,
            };
            Existing.Add(created);
            return Task.FromResult(created);
        }

        public Task<SslCertificateRepresentation> UpdateCertificateAsync(
            long certificateId, CreateOrUpdateSslCertificateBody body, CancellationToken cancellationToken = default)
        {
            Updated.Add((certificateId, body));
            var existing = Existing.FirstOrDefault(c => c.Id == certificateId)
                           ?? new SslCertificateRepresentation { Id = certificateId, Name = body.Name };
            return Task.FromResult(existing);
        }

        public Task DeleteCertificateAsync(long certificateId, CancellationToken cancellationToken = default)
        {
            Deleted.Add(certificateId);
            Existing.RemoveAll(c => c.Id == certificateId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DomainMapping>> GetDomainMappingsForCertificateAsync(
            long certificateId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DomainMapping>>(
                DomainMappings.TryGetValue(certificateId, out var mappings) ? mappings : new List<DomainMapping>());
    }
}
