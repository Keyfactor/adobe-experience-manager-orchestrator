using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
                    throw new CloudManagerApiException(
                        (int)response.StatusCode,
                        $"{method} {path} failed ({(int)response.StatusCode}): {Truncate(payload)}");
                }

                if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(payload))
                    return default;

                return JsonSerializer.Deserialize<T>(payload, AemcmJson.Options);
            }

            throw new CloudManagerApiException(401, $"{method} {path} failed: unauthorized after token refresh.");
        }

        private static string Truncate(string s, int max = 500) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    /// <summary>Carries the HTTP status code so callers can special-case (e.g. 404, 429).</summary>
    public class CloudManagerApiException : Exception
    {
        public int StatusCode { get; }

        public CloudManagerApiException(int statusCode, string message) : base(message) =>
            StatusCode = statusCode;
    }
}
