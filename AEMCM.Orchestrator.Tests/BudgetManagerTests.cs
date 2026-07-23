
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System.Collections.Generic;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client.Models;
using Keyfactor.Extensions.Orchestrator.AEMCM.Logic;
using Xunit;

namespace AEMCM.Orchestrator.Tests
{
    public class BudgetManagerTests
    {
        private static SslCertificateRepresentation Cert(
            long id, string type, string status, long expireAt) => new()
        {
            Id = id,
            SslCertificateType = type,
            SslCertificateStatus = status,
            ExpireAt = expireAt,
        };

        [Theory]
        [InlineData(0, true)]
        [InlineData(69, true)]
        [InlineData(70, false)]
        [InlineData(71, false)]
        public void HasBudgetForNew_RespectsLimit(int count, bool expected) =>
            Assert.Equal(expected, BudgetManager.HasBudgetForNew(count));

        [Theory]
        [InlineData(1, true)]
        [InlineData(100, true)]
        [InlineData(101, false)]
        public void IsWithinSanLimit_RespectsLimit(int sanCount, bool expected) =>
            Assert.Equal(expected, BudgetManager.IsWithinSanLimit(sanCount));

        [Fact]
        public void PickReclaimCandidate_ChoosesOldestExpiredCustomerManaged()
        {
            var existing = new[]
            {
                Cert(1, SslCertificateType.Ov, SslCertificateStatus.Valid, 5000),
                Cert(2, SslCertificateType.Ov, SslCertificateStatus.Expired, 3000), // oldest expired
                Cert(3, SslCertificateType.Ev, SslCertificateStatus.Expired, 4000),
                Cert(4, SslCertificateType.Dv, SslCertificateStatus.Expired, 1000), // DV — must be ignored
            };

            var candidate = BudgetManager.PickReclaimCandidate(existing);

            Assert.NotNull(candidate);
            Assert.Equal(2, candidate!.Id);
        }

        [Fact]
        public void PickReclaimCandidate_ReturnsNull_WhenNothingReclaimable()
        {
            var existing = new[]
            {
                Cert(1, SslCertificateType.Ov, SslCertificateStatus.Valid, 5000),
                Cert(2, SslCertificateType.Dv, SslCertificateStatus.Expired, 1000),
            };

            Assert.Null(BudgetManager.PickReclaimCandidate(existing));
        }
    }
}
