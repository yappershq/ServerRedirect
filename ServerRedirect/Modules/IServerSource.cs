using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ServerRedirect.Modules;

/// <summary>Abstraction over API and A2S data sources.</summary>
internal interface IServerSource
{
    Task<IReadOnlyList<ServerInfo>> FetchAsync(CancellationToken ct = default);
}
