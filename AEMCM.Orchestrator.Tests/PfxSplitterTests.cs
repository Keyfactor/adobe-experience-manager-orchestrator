
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System.Linq;
using System.Text.RegularExpressions;
using AEMCM.Orchestrator.Tests.TestHelpers;
using Keyfactor.Extensions.Orchestrator.AEMCM.Logic;
using Xunit;

namespace AEMCM.Orchestrator.Tests
{
    public class PfxSplitterTests
    {
        private static int CountCerts(string pem) =>
            Regex.Matches(pem ?? string.Empty, "BEGIN CERTIFICATE").Count;

        private static string Body(string pem) =>
            Regex.Replace(pem ?? string.Empty, "-----[^-]+-----|\\s", string.Empty);

        [Fact]
        public void Split_SelfSigned_ProducesLeafAndKey_WithEmptyChain()
        {
            var pfx = CertTestFactory.CreateSelfSignedRsaPfx("example.com", new[] { "example.com", "www.example.com" });

            var result = PfxSplitter.Split(pfx, CertTestFactory.DefaultPassword);

            Assert.Contains("BEGIN CERTIFICATE", result.CertificatePem);
            Assert.Contains("BEGIN PRIVATE KEY", result.PrivateKeyPkcs8Pem);   // PKCS#8, unencrypted
            Assert.DoesNotContain("ENCRYPTED", result.PrivateKeyPkcs8Pem);
            Assert.Equal(0, CountCerts(result.ChainPem));                       // no chain for self-signed
            Assert.Equal("RSA", result.KeyAlgorithm);
            Assert.Equal(2048, result.KeySize);
        }

        [Fact]
        public void Split_Chained_ExcludesLeafFromChain()
        {
            var pfx = CertTestFactory.CreateChainedRsaPfx("secure.example.com", new[] { "secure.example.com" });

            var result = PfxSplitter.Split(pfx, CertTestFactory.DefaultPassword);

            // Chain must contain the CA only (1 cert) and NOT the leaf.
            Assert.Equal(1, CountCerts(result.ChainPem));
            var leafBody = Body(result.CertificatePem);
            Assert.False(Body(result.ChainPem).Contains(leafBody),
                "Leaf certificate must be excluded from the chain field.");
        }

        [Fact]
        public void Split_ParsesDnsSans()
        {
            var pfx = CertTestFactory.CreateSelfSignedRsaPfx(
                "example.com", new[] { "example.com", "www.example.com", "api.example.com" });

            var result = PfxSplitter.Split(pfx, CertTestFactory.DefaultPassword);

            Assert.Equal(
                new[] { "api.example.com", "example.com", "www.example.com" },
                result.SubjectAlternativeNames.OrderBy(s => s).ToArray());
        }

        [Theory]
        [InlineData(256)]
        [InlineData(384)]
        public void Split_Ecdsa_ReportsAlgorithmAndSize(int bits)
        {
            var pfx = CertTestFactory.CreateSelfSignedEcdsaPfx("ec.example.com", new[] { "ec.example.com" }, bits);

            var result = PfxSplitter.Split(pfx, CertTestFactory.DefaultPassword);

            Assert.Equal("ECDSA", result.KeyAlgorithm);
            Assert.Equal(bits, result.KeySize);
        }

        [Fact]
        public void Split_EmptyInput_Throws()
        {
            Assert.ThrowsAny<System.Exception>(() => PfxSplitter.Split(System.Array.Empty<byte>(), "x"));
        }
    }
}
