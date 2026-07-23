
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Keyfactor.Extensions.Orchestrator.AEMCM;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client.Models;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AEMCM.Client
{
    /// <summary>
    /// HTTP implementation of <see cref="ICloudManagerClient"/> against
    /// https://cloudmanager.adobe.io, scoped to a single program.
    /// </summary>
    public class CloudManagerClient : ICloudManagerClient
    {
        private const int PageSize = 100; // API default is 20; page in larger chunks.

        private readonly HttpClient _http;
        private readonly IAdobeImsAuthClient _auth;
        private readonly ILogger _logger;
        private readonly string _baseUrl;
        private readonly long _programId;
        private readonly string _apiKey;    // IMS client id (x-api-key)
        private readonly string _imsOrgId;  // x-gw-ims-org-id

        public CloudManagerClient(
            HttpClient http,
            IAdobeImsAuthClient auth,
            ILogger logger,
            string baseUrl,
            long programId,
            string apiKey,
            string imsOrgId)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _baseUrl = (baseUrl ?? "https://cloudmanager.adobe.io").TrimEnd('/');
            _programId = programId;
            _apiKey = apiKey;
            _imsOrgId = imsOrgId;
        }

        public async Task<IReadOnlyList<SslCertificateRepresentation>> GetAllCertificatesAsync(
            string? sslCertificateTypeFilter = null,
            string? statusFilter = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<SslCertificateRepresentation>();
            var start = 0;

            while (true)
            {
                var query = new StringBuilder()
                    .Append("?start=").Append(start.ToString(CultureInfo.InvariantCulture))
                    .Append("&limit=").Append(PageSize.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(sslCertificateTypeFilter))
                    query.Append("&sslCertificateType=").Append(Uri.EscapeDataString(sslCertificateTypeFilter));
                if (!string.IsNullOrWhiteSpace(statusFilter))
                    query.Append("&status=").Append(Uri.EscapeDataString(statusFilter));

                var page = await SendAsync<AllSslCertificatesList>(
                    HttpMethod.Get,
                    $"/api/program/{_programId}/certificates{query}",
                    body: null,
                    cancellationToken).ConfigureAwait(false);

                var items = page?.Embedded?.Certificates ?? new List<SslCertificateRepresentation>();
                results.AddRange(items);

                // Stop when a short page is returned or there is no "next" link.
                var hasNext = page?.Links?.Next != null && !string.IsNullOrEmpty(page.Links.Next.Href);
                if (items.Count < PageSize || !hasNext) break;
                start += items.Count;
            }

            _logger.LogDebug("Retrieved {Count} certificate(s) for program {ProgramId}", results.Count, _programId);
            return results;
        }

        public Task<SslCertificateRepresentation?> GetCertificateAsync(
            long certificateId, CancellationToken cancellationToken = default) =>
            SendAsync<SslCertificateRepresentation>(
                HttpMethod.Get,
                $"/api/program/{_programId}/certificate/{certificateId}",
                body: null,
                cancellationToken);

        public async Task<SslCertificateRepresentation> CreateCertificateAsync(
            CreateOrUpdateSslCertificateBody body, CancellationToken cancellationToken = default) =>
            await SendAsync<SslCertificateRepresentation>(
                HttpMethod.Post,
                $"/api/program/{_programId}/certificates",
                body,
                cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Create certificate returned an empty response.");

        public async Task<SslCertificateRepresentation> UpdateCertificateAsync(
            long certificateId, CreateOrUpdateSslCertificateBody body,
            CancellationToken cancellationToken = default) =>
            await SendAsync<SslCertificateRepresentation>(
                HttpMethod.Put,
                $"/api/program/{_programId}/certificate/{certificateId}",
                body,
                cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Update certificate returned an empty response.");

        public async Task DeleteCertificateAsync(long certificateId, CancellationToken cancellationToken = default) =>
            await SendAsync<object>(
                HttpMethod.Delete,
                $"/api/program/{_programId}/certificate/{certificateId}",
                body: null,
                cancellationToken).ConfigureAwait(false);

        public async Task<IReadOnlyList<DomainMapping>> GetDomainMappingsForCertificateAsync(
            long certificateId, CancellationToken cancellationToken = default)
        {
            var list = await SendAsync<DomainMappingList>(
                HttpMethod.Get,
                $"/api/program/{_programId}/domain-mappings?certificateId={certificateId}",
                body: null,
                cancellationToken).ConfigureAwait(false);
            return list?.DomainMappings ?? new List<DomainMapping>();
        }

        private async Task<T?> SendAsync<T>(
            HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            // One automatic retry after refreshing the token on a 401.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

                using var request = new HttpRequestMessage(method, _baseUrl + path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
                request.Headers.TryAddWithoutValidation("x-gw-ims-org-id", _imsOrgId);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if (body != null)
                {
                    var json = JsonSerializer.Serialize(body, AemcmJson.Options);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0
                    && _auth is AdobeImsAuthClient concrete)
                {
                    _logger.LogWarning("Cloud Manager returned 401; refreshing IMS token and retrying once.");
                    concrete.Invalidate();
                    continue;
                }

                var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    // Log the full raw response for troubleshooting; surface a clean message to the job.
                    _logger.LogError("Cloud Manager {Method} {Path} failed ({Status}): {Body}",
                        method, path, statusCode, payload);
                    throw new CloudManagerApiException(
                        statusCode, FormatError(method, path, statusCode, payload), payload);
                }

                if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(payload))
                    return default;

                return JsonSerializer.Deserialize<T>(payload, AemcmJson.Options);
            }

            throw new CloudManagerApiException(401, $"{method} {path} failed: unauthorized after token refresh.");
        }

        private static string Truncate(string s, int max = 500) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

        /// <summary>
        /// Translates a Cloud Manager error response into a concise, operator-friendly message.
        /// The OV/EV policy rejection is special-cased into a single clear sentence; other
        /// validation errors are surfaced as their distinct messages. Falls back to a truncated
        /// raw body when the payload isn't a recognized error shape.
        /// </summary>
        internal static string FormatError(HttpMethod method, string path, int statusCode, string payload)
        {
            CloudManagerErrorResponse? error = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(payload))
                    error = JsonSerializer.Deserialize<CloudManagerErrorResponse>(payload, AemcmJson.Options);
            }
            catch (JsonException)
            {
                // Not a JSON error body — fall through to the raw fallback.
            }

            var errors = error?.AdditionalProperties?.Errors;
            if (errors != null && errors.Count > 0)
            {
                // The certificate-policy rejection is the common, actionable case — keep it simple.
                if (errors.Any(e => string.Equals(e.Code, "INVALID_CERTIFICATE_POLICY", StringComparison.OrdinalIgnoreCase)))
                {
                    return "The certificate is not supported: AEM Cloud Manager requires an OV or EV certificate. " +
                           "DV and self-signed certificates are rejected.";
                }

                var messages = errors
                    .Select(e => e.Message)
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (messages.Count > 0)
                {
                    var title = string.IsNullOrWhiteSpace(error!.Title) ? "Cloud Manager rejected the request" : error.Title!;
                    return $"{title}: {string.Join("; ", messages)}.";
                }
            }

            return $"{method} {path} failed ({statusCode}): {Truncate(payload)}";
        }
    }

    /// <summary>Cloud Manager RFC-7807-style error response with a nested validation-errors list.</summary>
    internal sealed class CloudManagerErrorResponse
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("status")] public int Status { get; set; }
        [JsonPropertyName("additionalProperties")] public CloudManagerErrorDetails? AdditionalProperties { get; set; }
    }

    internal sealed class CloudManagerErrorDetails
    {
        [JsonPropertyName("errors")] public List<CloudManagerFieldError> Errors { get; set; } = new();
    }

    internal sealed class CloudManagerFieldError
    {
        [JsonPropertyName("field")] public string? Field { get; set; }
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }

    /// <summary>Carries the HTTP status code and raw response body so callers can special-case or log.</summary>
    public class CloudManagerApiException : Exception
    {
        public int StatusCode { get; }
        public string? ResponseBody { get; }

        public CloudManagerApiException(int statusCode, string message, string? responseBody = null) : base(message)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }
    }
}
