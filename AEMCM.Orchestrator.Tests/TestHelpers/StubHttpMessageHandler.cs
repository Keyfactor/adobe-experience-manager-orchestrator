//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AEMCM.Orchestrator.Tests.TestHelpers
{
    /// <summary>A captured outgoing request (method, URI, body, and headers) for assertions.</summary>
    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, HttpRequestMessage Message);

    /// <summary>
    /// Test double for <see cref="HttpMessageHandler"/> that lets a test drive
    /// <see cref="CloudManagerClient"/>/<see cref="AdobeImsAuthClient"/> over the real HTTP/serialization
    /// path without a network. The responder receives the request and its (already-read) body string.
    /// </summary>
    public sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

        public List<RecordedRequest> Requests { get; } = new();

        public StubHttpMessageHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync().ConfigureAwait(false);

            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body, request));
            return _responder(request, body);
        }

        public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        public static HttpResponseMessage Empty(HttpStatusCode status) => new(status);
    }
}
