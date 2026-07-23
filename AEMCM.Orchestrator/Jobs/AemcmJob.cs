
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Net.Http;
using System.Text.Json;
using Keyfactor.Extensions.Orchestrator.AEMCM.Client;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AEMCM
{
    /// <summary>
    /// Non-generic holder for the shared <see cref="HttpClient"/>. A static field on a generic
    /// type is instantiated once per closed generic type, so it lives here instead.
    /// </summary>
    internal static class AemcmHttp
    {
        // HttpClient is thread-safe and intended to be long-lived; shared across all jobs.
        public static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(100) };
    }

    /// <summary>
    /// Base class for the AEM Cloud Manager jobs. Holds the shared logger, resolved
    /// <see cref="AemcmProperties"/>, the IMS auth client, and the program-scoped
    /// <see cref="ICloudManagerClient"/>, and centralizes config/credential parsing.
    /// </summary>
    /// <typeparam name="T">The concrete job type (used by derived classes for the class logger).</typeparam>
    public abstract class AemcmJob<T> : IOrchestratorJobExtension
    {
        protected static HttpClient Http => AemcmHttp.Client;

        public string ExtensionName => AemcmConstants.StoreTypeName;

        protected internal virtual ICloudManagerClient? Client { get; set; }
        protected internal virtual IAdobeImsAuthClient? Auth { get; set; }
        protected internal virtual AemcmProperties Properties { get; set; } = new();
        protected internal IPAMSecretResolver? PamSecretResolver { get; set; }
        protected internal ILogger Logger { get; set; } = null!;

        /// <summary>
        /// Parses store credentials, store path, and custom fields into <see cref="Properties"/>,
        /// then builds the IMS auth client and (for store jobs) the program-scoped Cloud Manager client.
        /// </summary>
        public virtual void InitializeStore(dynamic config)
        {
            try
            {
                Properties = new AemcmProperties
                {
                    ClientId = PamUtilities.ResolvePamField(PamSecretResolver, Logger, "Server UserName", config.ServerUsername),
                    ClientSecret = PamUtilities.ResolvePamField(PamSecretResolver, Logger, "Server Password", config.ServerPassword),
                };

                // Discovery jobs carry ClientMachine at the top level.
                if (ConfigHasProperty(config, "ClientMachine"))
                {
                    string? clientMachine = config.ClientMachine;
                    if (!string.IsNullOrEmpty(clientMachine)) Properties.BaseUrl = clientMachine!;
                }

                // Inventory/Management jobs carry the store details.
                if (ConfigHasProperty(config, "CertificateStoreDetails"))
                {
                    var store = config.CertificateStoreDetails;

                    string? baseUrl = store?.ClientMachine;
                    if (!string.IsNullOrEmpty(baseUrl)) Properties.BaseUrl = baseUrl!;

                    Properties.StorePath = store?.StorePath;

                    string propsJson = store?.Properties?.ToString() ?? "{}";
                    var custom = JsonSerializer.Deserialize<StoreCustomFields>(propsJson, AemcmJson.Options) ?? new StoreCustomFields();
                    Properties.ImsOrgId = custom.ImsOrgId ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(custom.ImsTokenUrl)) Properties.ImsTokenUrl = custom.ImsTokenUrl!;
                    if (!string.IsNullOrWhiteSpace(custom.ImsScopes)) Properties.ImsScopes = custom.ImsScopes!;

                    Properties.ProgramId = ParseProgramId(Properties.StorePath);
                }

                Auth ??= new AdobeImsAuthClient(
                    Http, Logger, Properties.ImsTokenUrl, Properties.ClientId, Properties.ClientSecret, Properties.ImsScopes);

                if (Properties.ProgramId > 0)
                {
                    Client ??= new CloudManagerClient(
                        Http, Auth, Logger, Properties.BaseUrl, Properties.ProgramId,
                        apiKey: Properties.ClientId, imsOrgId: Properties.ImsOrgId);
                }

                Logger.LogTrace("AEMCM store initialization complete (programId={ProgramId}).", Properties.ProgramId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error initializing AEMCM store");
                throw;
            }
        }

        private static bool ConfigHasProperty(object config, string name) =>
            config.GetType().GetProperty(name) != null;

        private static long ParseProgramId(string? storePath) =>
            long.TryParse(storePath, out var id)
                ? id
                : throw new ArgumentException($"Store Path must be a numeric Cloud Manager programId; got '{storePath}'.");
    }
}
