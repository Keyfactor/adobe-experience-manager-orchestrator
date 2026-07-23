
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using AEMCM.Orchestrator.Tests.TestHelpers;
using Keyfactor.Extensions.Orchestrator.AEMCM;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client.Models;
using Keyfactor.Orchestrators.Common.Enums;
using Xunit;

namespace AEMCM.Orchestrator.Tests
{
    public class ManagementTests
    {
        private const long JobId = 42;

        private static (Management job, FakeCloudManagerClient fake) BuildJob()
        {
            var fake = new FakeCloudManagerClient();
            var job = new Management(resolver: null!) { Client = fake };
            return (job, fake);
        }

        private static string Pfx(int keySize = 2048, params string[] sans) =>
            Convert.ToBase64String(CertTestFactory.CreateSelfSignedRsaPfx("cert.example.com",
                sans.Length > 0 ? sans : new[] { "cert.example.com" }, keySize));

        private static string EcPfx(int bits = 256) =>
            Convert.ToBase64String(CertTestFactory.CreateSelfSignedEcdsaPfx("ec.example.com",
                new[] { "ec.example.com" }, bits));

        private static SslCertificateRepresentation Cert(
            long id, string name, string type = SslCertificateType.Ov, params string[] sans) => new()
        {
            Id = id,
            Name = name,
            SslCertificateType = type,
            SslCertificateStatus = SslCertificateStatus.Valid,
            SubjectAlternativeNames = sans.ToList(),
        };

        // ── Add ───────────────────────────────────────────────────────────────

        [Fact]
        public void Add_NewCert_CreatesWithSplitBody()
        {
            var (job, fake) = BuildJob();

            var result = job.PerformAddition("my-cert", Pfx(), CertTestFactory.DefaultPassword,
                overwrite: false, JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Success, result.Result);
            Assert.Single(fake.Created);
            Assert.Empty(fake.Updated);
            Assert.Equal("my-cert", fake.Created[0].Name);
            Assert.Contains("BEGIN PRIVATE KEY", fake.Created[0].PrivateKey.Value);
            Assert.Contains("BEGIN CERTIFICATE", fake.Created[0].Certificate);
        }

        [Fact]
        public void Add_Overwrite_MatchByAlias_UpdatesInPlace()
        {
            var (job, fake) = BuildJob();
            fake.Existing.Add(Cert(5, "my-cert"));

            var result = job.PerformAddition("my-cert", Pfx(), CertTestFactory.DefaultPassword,
                overwrite: true, JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Success, result.Result);
            Assert.Empty(fake.Created);
            Assert.Single(fake.Updated);
            Assert.Equal(5, fake.Updated[0].Id);
        }

        [Fact]
        public void Add_DuplicateName_NoOverwrite_Fails()
        {
            var (job, fake) = BuildJob();
            fake.Existing.Add(Cert(5, "my-cert"));

            var result = job.PerformAddition("my-cert", Pfx(), CertTestFactory.DefaultPassword,
                overwrite: false, JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Failure, result.Result);
            Assert.Contains("already exists", result.FailureMessage);
            Assert.Empty(fake.Created);
            Assert.Empty(fake.Updated);
        }

        [Fact]
        public void Add_AtCertificateLimit_Fails()
        {
            var (job, fake) = BuildJob();
            for (var i = 0; i < BudgetManagerLimit; i++)
                fake.Existing.Add(Cert(i + 1, $"cert-{i}"));

            var result = job.PerformAddition("brand-new", Pfx(2048, "brand-new.example.com"),
                CertTestFactory.DefaultPassword, overwrite: false, JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Failure, result.Result);
            Assert.Contains("limit", result.FailureMessage);
            Assert.Empty(fake.Created);
        }

        [Fact]
        public void Add_AliasResolvesToDvCert_Fails()
        {
            var (job, fake) = BuildJob();
            fake.Existing.Add(Cert(9, "dv-cert", SslCertificateType.Dv));

            var result = job.PerformAddition("dv-cert", Pfx(), CertTestFactory.DefaultPassword,
                overwrite: true, JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Failure, result.Result);
            Assert.Contains("Adobe-managed", result.FailureMessage);
            Assert.Empty(fake.Created);
            Assert.Empty(fake.Updated);
        }

        [Fact]
        public void Add_Rsa4096_RejectedByPolicy()
        {
            var (job, fake) = BuildJob();

            var result = job.PerformAddition("big-rsa", Pfx(4096), CertTestFactory.DefaultPassword,
                overwrite: false, JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Failure, result.Result);
            Assert.Contains("RSA key size", result.FailureMessage);
            Assert.Empty(fake.Created);
        }

        [Fact]
        public void Add_Ecdsa_Succeeds()
        {
            var (job, fake) = BuildJob();

            var result = job.PerformAddition("ec-cert", EcPfx(256), CertTestFactory.DefaultPassword,
                overwrite: false, JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Success, result.Result);
            Assert.Single(fake.Created);
        }

        [Fact]
        public void Add_NoContents_Fails()
        {
            var (job, _) = BuildJob();

            var result = job.PerformAddition("x", contents: "", CertTestFactory.DefaultPassword,
                overwrite: false, JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Failure, result.Result);
            Assert.Contains("contents", result.FailureMessage);
        }

        // ── Remove ─────────────────────────────────────────────────────────────

        [Fact]
        public void Remove_ExistingUnusedCert_Deletes()
        {
            var (job, fake) = BuildJob();
            fake.Existing.Add(Cert(5, "my-cert"));

            var result = job.PerformRemoval("my-cert", JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Success, result.Result);
            Assert.Contains(5, fake.Deleted);
        }

        [Fact]
        public void Remove_CertInUseByDomainMapping_Blocked()
        {
            var (job, fake) = BuildJob();
            fake.Existing.Add(Cert(5, "my-cert"));
            fake.DomainMappings[5] = new List<DomainMapping>
            {
                new() { DomainMappingId = 1, CertificateId = 5, DomainName = "www.example.com" },
            };

            var result = job.PerformRemoval("my-cert", JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Failure, result.Result);
            Assert.Contains("in use", result.FailureMessage);
            Assert.Empty(fake.Deleted);
        }

        [Fact]
        public void Remove_NoMatch_TreatedAsAlreadyRemoved()
        {
            var (job, fake) = BuildJob();

            var result = job.PerformRemoval("ghost", JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Success, result.Result);
            Assert.Empty(fake.Deleted);
        }

        [Fact]
        public void Remove_AmbiguousAlias_Fails()
        {
            var (job, fake) = BuildJob();
            fake.Existing.Add(Cert(1, "dup"));
            fake.Existing.Add(Cert(2, "dup"));

            var result = job.PerformRemoval("dup", JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Failure, result.Result);
            Assert.Contains("multiple", result.FailureMessage);
            Assert.Empty(fake.Deleted);
        }

        [Fact]
        public void Remove_DvCert_Refused()
        {
            var (job, fake) = BuildJob();
            fake.Existing.Add(Cert(9, "dv-cert", SslCertificateType.Dv));

            var result = job.PerformRemoval("dv-cert", JobId);

            Assert.Equal(OrchestratorJobStatusJobResult.Failure, result.Result);
            Assert.Contains("Adobe-managed", result.FailureMessage);
            Assert.Empty(fake.Deleted);
        }

        private const int BudgetManagerLimit = 70;
    }
}
