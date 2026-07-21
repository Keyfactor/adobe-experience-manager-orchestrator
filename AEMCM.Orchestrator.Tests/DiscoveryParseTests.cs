using Keyfactor.Extensions.Orchestrator.AEMCM;
using Xunit;

namespace AEMCM.Orchestrator.Tests
{
    public class DiscoveryParseTests
    {
        [Fact]
        public void ParseProgramIds_ReadsEmbeddedProgramIds()
        {
            const string payload = @"{
              ""_embedded"": {
                ""programs"": [
                  { ""id"": ""12345"", ""name"": ""Prod"" },
                  { ""id"": ""67890"", ""name"": ""Stage"" }
                ]
              }
            }";

            var ids = Discovery.ParseProgramIds(payload);

            Assert.Equal(new[] { "12345", "67890" }, ids);
        }

        [Fact]
        public void ParseProgramIds_EmptyOrMissing_ReturnsEmpty()
        {
            Assert.Empty(Discovery.ParseProgramIds("{}"));
            Assert.Empty(Discovery.ParseProgramIds(@"{ ""_embedded"": { ""programs"": [] } }"));
        }
    }
}
