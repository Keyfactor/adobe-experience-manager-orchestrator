using System.Collections.Generic;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client.Models;
using Keyfactor.Extensions.Orchestrator.AEMCM.Logic;
using Xunit;

namespace AEMCM.Orchestrator.Tests
{
    public class CertMatcherTests
    {
        private static SslCertificateRepresentation Cert(
            long id, string name, string type, params string[] sans) => new()
        {
            Id = id,
            Name = name,
            SslCertificateType = type,
            SubjectAlternativeNames = new List<string>(sans),
        };

        [Fact]
        public void ExactSanSet_Matches_RegardlessOfOrderOrCase()
        {
            var existing = new[] { Cert(1, "wildcard", SslCertificateType.Ov, "a.example.com", "b.example.com") };

            var result = CertMatcher.FindMatch(
                existing, new[] { "B.EXAMPLE.COM", "a.example.com" }, alias: null, overwrite: false);

            Assert.Equal(CertMatchType.ExactSanSet, result.MatchType);
            Assert.Equal(1, result.Certificate!.Id);
        }

        [Fact]
        public void AliasMatch_OnlyWhenOverwrite()
        {
            var existing = new[] { Cert(7, "my-cert", SslCertificateType.Ev, "x.example.com") };

            var noOverwrite = CertMatcher.FindMatch(existing, new[] { "different.example.com" }, "my-cert", overwrite: false);
            Assert.False(noOverwrite.IsMatch);

            var withOverwrite = CertMatcher.FindMatch(existing, new[] { "different.example.com" }, "my-cert", overwrite: true);
            Assert.Equal(CertMatchType.Alias, withOverwrite.MatchType);
            Assert.Equal(7, withOverwrite.Certificate!.Id);
        }

        [Fact]
        public void AliasMatch_ByNumericId()
        {
            var existing = new[] { Cert(42, "friendly", SslCertificateType.Ov, "x.example.com") };

            var result = CertMatcher.FindMatch(existing, new[] { "new.example.com" }, "42", overwrite: true);

            Assert.Equal(CertMatchType.Alias, result.MatchType);
            Assert.Equal(42, result.Certificate!.Id);
        }

        [Fact]
        public void AdobeManagedDv_IsNeverMatched()
        {
            var existing = new[] { Cert(9, "dv", SslCertificateType.Dv, "a.example.com") };

            var result = CertMatcher.FindMatch(existing, new[] { "a.example.com" }, "dv", overwrite: true);

            Assert.False(result.IsMatch);
        }

        [Fact]
        public void Superset_MatchesOnlyWhenEnabled()
        {
            var existing = new[] { Cert(3, "base", SslCertificateType.Ov, "a.example.com") };
            var incoming = new[] { "a.example.com", "b.example.com" }; // superset of existing

            var disabled = CertMatcher.FindMatch(existing, incoming, alias: null, overwrite: false, allowSuperset: false);
            Assert.False(disabled.IsMatch);

            var enabled = CertMatcher.FindMatch(existing, incoming, alias: null, overwrite: false, allowSuperset: true);
            Assert.Equal(CertMatchType.SanSuperset, enabled.MatchType);
            Assert.Equal(3, enabled.Certificate!.Id);
        }

        [Fact]
        public void NoCandidates_ReturnsNoMatch()
        {
            var result = CertMatcher.FindMatch(
                System.Array.Empty<SslCertificateRepresentation>(),
                new[] { "a.example.com" }, alias: "x", overwrite: true);

            Assert.Equal(CertMatchType.None, result.MatchType);
            Assert.Null(result.Certificate);
        }
    }
}
