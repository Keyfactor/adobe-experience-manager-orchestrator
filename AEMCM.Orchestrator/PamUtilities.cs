
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AEMCM
{
    /// <summary>
    /// Resolves store field values through the orchestrator's PAM provider when configured,
    /// otherwise returns the value as-is.
    /// </summary>
    public static class PamUtilities
    {
        public static string ResolvePamField(
            IPAMSecretResolver? resolver, ILogger logger, string fieldName, string? value)
        {
            logger.LogTrace("Resolving PAM field {FieldName}", fieldName);
            var resolved = resolver?.Resolve(value);
            return resolved ?? value ?? string.Empty;
        }
    }
}
