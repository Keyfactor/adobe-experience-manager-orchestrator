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
