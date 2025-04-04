using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.Telemetry;

public interface ITelemetrySink {
	Task Flush(JsonObject data, CancellationToken token);
}
