
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

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

        [Fact]
        public void ParseTenantIds_ReadsEmbeddedTenantIds()
        {
            const string payload = @"{
              ""_embedded"": {
                ""tenants"": [
                  { ""id"": ""14"", ""imsTenantId"": ""acmeCorp"" },
                  { ""id"": ""15"", ""imsTenantId"": ""globex"" }
                ]
              }
            }";

            var ids = Discovery.ParseTenantIds(payload);

            Assert.Equal(new[] { "14", "15" }, ids);
        }

        [Fact]
        public void ParseTenantIds_EmptyOrMissing_ReturnsEmpty()
        {
            Assert.Empty(Discovery.ParseTenantIds("{}"));
            Assert.Empty(Discovery.ParseTenantIds(@"{ ""_embedded"": { ""tenants"": [] } }"));
        }
    }
}
