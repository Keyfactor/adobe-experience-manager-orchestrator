//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AEMCM.Orchestrator.Tests.TestHelpers;
using Keyfactor.Extensions.Orchestrator.AEMCM;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AEMCM.Orchestrator.Tests
{
    /// <summary>
    /// Exercises the real <see cref="CloudManagerClient"/> over the HTTP/serialization layer
    /// (via a stubbed handler) — covering the write paths Adobe won't let us drive live.
    /// </summary>
    public class CloudManagerClientTests
    {
        private const long ProgramId = 211102;

        private sealed class StubAuth : IAdobeImsAuthClient
        {
            public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult("test-token");
        }

        private static CloudManagerClient BuildClient(StubHttpMessageHandler handler, IAdobeImsAuthClient? auth = null) =>
            new(new HttpClient(handler), auth ?? new StubAuth(), NullLogger.Instance,
                "https://cloudmanager.adobe.io", ProgramId, apiKey: "api-key-123", imsOrgId: "org@AdobeOrg");

        private static CreateOrUpdateSslCertificateBody SampleBody() => new()
        {
            Name = "my-cert",
            Certificate = "-----BEGIN CERTIFICATE-----AAA-----END CERTIFICATE-----",
            PrivateKey = new PrivateKeyValue { Value = "-----BEGIN PRIVATE KEY-----BBB-----END PRIVATE KEY-----" },
            Chain = "-----BEGIN CERTIFICATE-----CCC-----END CERTIFICATE-----",
        };

        private static string Serialize(object o) => JsonSerializer.Serialize(o, AemcmJson.Options);

        private static AllSslCertificatesList BuildList(int count, bool hasNext, int startId = 0)
        {
            var list = new AllSslCertificatesList { TotalNumberOfItems = count };
            for (var i = 0; i < count; i++)
            {
                list.Embedded.Certificates.Add(new SslCertificateRepresentation
                {
                    Id = startId + i,
                    Name = $"c{startId + i}",
                    SslCertificateType = SslCertificateType.Ov,
                    SslCertificateStatus = SslCertificateStatus.Valid,
                });
            }
            if (hasNext)
                list.Links.Next = new HalLink { Href = $"/api/program/{ProgramId}/certificates?start=100&limit=100" };
            return list;
        }

        [Fact]
        public void CreateCertificate_PostsExpectedBody_AndParsesResponse()
        {
            var handler = new StubHttpMessageHandler((_, _) =>
                StubHttpMessageHandler.Json(HttpStatusCode.Created,
                    "{\"id\":777,\"name\":\"my-cert\",\"sslCertificateType\":\"OV\"}"));
            var client = BuildClient(handler);

            var result = client.CreateCertificateAsync(SampleBody()).GetAwaiter().GetResult();

            Assert.Equal(777, result.Id);
            var req = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.EndsWith($"/api/program/{ProgramId}/certificates", req.Uri.AbsolutePath);
            // Body field names must match the Cloud Manager contract (note the nested privateKey.value).
            Assert.NotNull(req.Body);
            Assert.Contains("\"name\":\"my-cert\"", req.Body!);
            Assert.Contains("\"certificate\":", req.Body!);
            Assert.Contains("\"privateKey\":{\"value\":", req.Body!);
            Assert.Contains("\"chain\":", req.Body!);
            // Auth + Adobe headers present.
            Assert.Equal("Bearer", req.Message.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", req.Message.Headers.Authorization!.Parameter);
            Assert.True(req.Message.Headers.Contains("x-api-key"));
            Assert.True(req.Message.Headers.Contains("x-gw-ims-org-id"));
        }

        [Fact]
        public void UpdateCertificate_PutsToIdPath()
        {
            var handler = new StubHttpMessageHandler((_, _) =>
                StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":55,\"name\":\"my-cert\"}"));
            var client = BuildClient(handler);

            var result = client.UpdateCertificateAsync(55, SampleBody()).GetAwaiter().GetResult();

            Assert.Equal(55, result.Id);
            var req = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.EndsWith($"/api/program/{ProgramId}/certificate/55", req.Uri.AbsolutePath);
        }

        [Fact]
        public void DeleteCertificate_SendsDeleteToIdPath()
        {
            var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Empty(HttpStatusCode.OK));
            var client = BuildClient(handler);

            client.DeleteCertificateAsync(55).GetAwaiter().GetResult();

            var req = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.EndsWith($"/api/program/{ProgramId}/certificate/55", req.Uri.AbsolutePath);
        }

        [Fact]
        public void GetAllCertificates_PagesAcrossMultiplePages()
        {
            var handler = new StubHttpMessageHandler((req, _) =>
                req.RequestUri!.Query.Contains("start=0")
                    ? StubHttpMessageHandler.Json(HttpStatusCode.OK, Serialize(BuildList(100, hasNext: true)))
                    : StubHttpMessageHandler.Json(HttpStatusCode.OK, Serialize(BuildList(1, hasNext: false, startId: 1000))));
            var client = BuildClient(handler);

            var certs = client.GetAllCertificatesAsync().GetAwaiter().GetResult();

            Assert.Equal(101, certs.Count);
            Assert.Equal(2, handler.Requests.Count);
            Assert.Contains(handler.Requests, r => r.Uri.Query.Contains("start=0"));
            Assert.Contains(handler.Requests, r => r.Uri.Query.Contains("start=100"));
        }

        [Fact]
        public void GetDomainMappingsForCertificate_FiltersByIdAndParses()
        {
            const string json =
                "{\"totalNumberOfItems\":1,\"domainMappings\":[{\"domainMappingId\":1,\"programId\":211102,\"certificateId\":55,\"domainName\":\"www.example.com\"}]}";
            var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, json));
            var client = BuildClient(handler);

            var mappings = client.GetDomainMappingsForCertificateAsync(55).GetAwaiter().GetResult();

            var mapping = Assert.Single(mappings);
            Assert.Equal("www.example.com", mapping.DomainName);
            var req = Assert.Single(handler.Requests);
            Assert.EndsWith($"/api/program/{ProgramId}/domain-mappings", req.Uri.AbsolutePath);
            Assert.Contains("certificateId=55", req.Uri.Query);
        }

        [Fact]
        public void ErrorResponse_PolicyRejection_ThrowsFriendlyException()
        {
            const string errorBody = "{\"status\":400,\"title\":\"SSL Certificate validation error\"," +
                "\"additionalProperties\":{\"errors\":[" +
                "{\"field\":\"certificate\",\"code\":\"INVALID_CERTIFICATE_SIGNATURE\",\"message\":\"Signature of the certificate or chain is corrupt or invalid\"}," +
                "{\"field\":\"certificate\",\"code\":\"INVALID_CERTIFICATE_POLICY\",\"message\":\"Certificate policy must be EV or OV, not DV.\"}]}}";
            var handler = new StubHttpMessageHandler((_, _) =>
                StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, errorBody));
            var client = BuildClient(handler);

            var ex = Assert.Throws<CloudManagerApiException>(() =>
                client.CreateCertificateAsync(SampleBody()).GetAwaiter().GetResult());

            Assert.Equal(400, ex.StatusCode);
            Assert.Contains("OV or EV", ex.Message);
            Assert.DoesNotContain("Signature", ex.Message);
            Assert.NotNull(ex.ResponseBody);
            Assert.Contains("INVALID_CERTIFICATE_POLICY", ex.ResponseBody!); // raw body preserved for logs
        }

        [Fact]
        public void Unauthorized_RefreshesTokenAndRetriesOnce()
        {
            var tokenCalls = 0;
            var cmCalls = 0;
            var handler = new StubHttpMessageHandler((req, _) =>
            {
                if (req.RequestUri!.AbsoluteUri.Contains("/ims/token"))
                {
                    tokenCalls++;
                    return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                        "{\"access_token\":\"tok\",\"token_type\":\"bearer\",\"expires_in\":3600}");
                }

                cmCalls++;
                return cmCalls == 1
                    ? StubHttpMessageHandler.Empty(HttpStatusCode.Unauthorized)
                    : StubHttpMessageHandler.Json(HttpStatusCode.OK, Serialize(BuildList(0, hasNext: false)));
            });

            var http = new HttpClient(handler);
            var auth = new AdobeImsAuthClient(http, NullLogger.Instance,
                "https://ims.example/ims/token/v3", "client-id", "client-secret", "openid");
            var client = new CloudManagerClient(http, auth, NullLogger.Instance,
                "https://cloudmanager.adobe.io", ProgramId, "client-id", "org@AdobeOrg");

            var certs = client.GetAllCertificatesAsync().GetAwaiter().GetResult();

            Assert.Empty(certs);
            Assert.Equal(2, cmCalls);    // 401, then success on retry
            Assert.Equal(2, tokenCalls); // initial token + refresh after 401
        }
    }
}
