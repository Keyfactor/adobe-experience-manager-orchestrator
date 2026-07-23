//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System.Net.Http;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client;
using Xunit;

namespace AEMCM.Orchestrator.Tests
{
    public class CloudManagerErrorTests
    {
        [Fact]
        public void FormatError_PolicyRejection_ReturnsSimpleOvEvMessage()
        {
            // The exact shape Cloud Manager returned during testing (policy + signature errors together).
            const string payload = @"{
              ""type"": ""about:blank"",
              ""status"": 400,
              ""title"": ""SSL Certificate validation error"",
              ""additionalProperties"": {
                ""errors"": [
                  { ""field"": ""certificate"", ""code"": ""INVALID_CERTIFICATE_SIGNATURE"", ""message"": ""Signature of the certificate or chain is corrupt or invalid"" },
                  { ""field"": ""chain"", ""code"": ""INVALID_CERTIFICATE_SIGNATURE"", ""message"": ""Signature of the certificate or chain is corrupt or invalid"" },
                  { ""field"": ""certificate"", ""code"": ""INVALID_CERTIFICATE_POLICY"", ""message"": ""Certificate policy must be EV or OV, not DV."" }
                ]
              }
            }";

            var message = CloudManagerClient.FormatError(HttpMethod.Post, "/api/program/1/certificates", 400, payload);

            Assert.Contains("OV or EV", message);
            Assert.Contains("DV", message);
            // Keep it simple — the raw signature noise and JSON should not leak into the job message.
            Assert.DoesNotContain("Signature", message);
            Assert.DoesNotContain("INVALID_CERTIFICATE", message);
            Assert.DoesNotContain("{", message);
        }

        [Fact]
        public void FormatError_NonPolicyValidationErrors_SurfacesDistinctMessages()
        {
            const string payload = @"{
              ""status"": 400,
              ""title"": ""SSL Certificate validation error"",
              ""additionalProperties"": {
                ""errors"": [
                  { ""field"": ""privateKey"", ""code"": ""INVALID_PRIVATE_KEY"", ""message"": ""Private key does not match the certificate"" }
                ]
              }
            }";

            var message = CloudManagerClient.FormatError(HttpMethod.Post, "/api/program/1/certificates", 400, payload);

            Assert.Contains("Private key does not match the certificate", message);
            Assert.Contains("SSL Certificate validation error", message);
        }

        [Fact]
        public void FormatError_UnrecognizedBody_FallsBackToRawSummary()
        {
            var message = CloudManagerClient.FormatError(HttpMethod.Get, "/api/program/1/certificates", 500, "Internal Server Error");

            Assert.Contains("/api/program/1/certificates", message);
            Assert.Contains("500", message);
            Assert.Contains("Internal Server Error", message);
        }
    }
}
