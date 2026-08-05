#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace KurrentDB.Core.Hosting;

public interface ISystemReadinessProbe {
    ValueTask<NodeSystemInfo> WaitUntilReady(CancellationToken cancellationToken);
}