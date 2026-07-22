
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Keyfactor.Extensions.Orchestrator.AEMCM;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client.Models;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AEMCM.Client
{
    /// <summary>
    /// Adobe IMS OAuth Server-to-Server (client credentials) token client with in-memory caching.
    /// JWT auth is deprecated; this uses grant_type=client_credentials against the IMS token endpoint.
    /// </summary>
    public class AdobeImsAuthClient : IAdobeImsAuthClient
    {
        // Refresh a little early to avoid using a token that expires mid-request.
        private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(60);

        private readonly HttpClient _http;
        private readonly ILogger _logger;
        private readonly string _tokenUrl;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _scopes;

        private readonly SemaphoreSlim _lock = new(1, 1);
        private string? _cachedToken;
        private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

        public AdobeImsAuthClient(
            HttpClient http,
            ILogger logger,
            string tokenUrl,
            string clientId,
            string clientSecret,
            string scopes)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tokenUrl = tokenUrl;
            _clientId = clientId;
            _clientSecret = clientSecret;
            _scopes = scopes;
        }

        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            if (IsCacheValid()) return _cachedToken!;

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsCacheValid()) return _cachedToken!;

                _logger.LogDebug("Requesting new Adobe IMS access token from {TokenUrl}", _tokenUrl);

                using var request = new HttpRequestMessage(HttpMethod.Post, _tokenUrl)
                {
                    Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("grant_type", "client_credentials"),
                        new KeyValuePair<string, string>("client_id", _clientId),
                        new KeyValuePair<string, string>("client_secret", _clientSecret),
                        new KeyValuePair<string, string>("scope", NormalizeScopes(_scopes)),
                    })
                };

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Adobe IMS token request failed ({(int)response.StatusCode}): {body}");
                }

                var token = JsonSerializer.Deserialize<ImsTokenResponse>(body, AemcmJson.Options)
                            ?? throw new InvalidOperationException("Empty IMS token response.");

                if (string.IsNullOrEmpty(token.AccessToken))
                    throw new InvalidOperationException("IMS token response contained no access_token.");

                _cachedToken = token.AccessToken;
                // OAuth Server-to-Server returns expires_in in seconds (typically 86399 ≈ 24h).
                _expiresAt = DateTimeOffset.UtcNow
                             + TimeSpan.FromSeconds(token.ExpiresInSeconds)
                             - ExpiryBuffer;

                _logger.LogDebug("Obtained IMS token; valid until {ExpiresAt:o}", _expiresAt);
                return _cachedToken;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>Forces the next call to fetch a fresh token (e.g. after a 401).</summary>
        public void Invalidate() => _expiresAt = DateTimeOffset.MinValue;

        private bool IsCacheValid() =>
            !string.IsNullOrEmpty(_cachedToken) && DateTimeOffset.UtcNow < _expiresAt;

        private static string NormalizeScopes(string scopes) =>
            string.IsNullOrWhiteSpace(scopes) ? string.Empty : scopes.Replace(" ", string.Empty);
    }
}
