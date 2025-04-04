using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.Telemetry;

public class InMemoryTelemetrySink : ITelemetrySink {
	public JsonObject Data { get; private set; }

	public Task Flush(JsonObject data, CancellationToken token) {
		Data = (JsonObject)JsonNode.Parse(data.ToJsonString());
		return Task.CompletedTask;
	}
}
