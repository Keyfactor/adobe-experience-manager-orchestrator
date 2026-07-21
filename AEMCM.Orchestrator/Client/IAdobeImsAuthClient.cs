using System.Threading;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.AEMCM.Client
{
    /// <summary>Obtains (and caches) Adobe IMS OAuth Server-to-Server access tokens.</summary>
    public interface IAdobeImsAuthClient
    {
        /// <summary>Returns a valid bearer token, refreshing from IMS when the cache is empty or expired.</summary>
        Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    }
}
