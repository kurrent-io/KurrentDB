using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DuckDB.NET.Data.DataChunk.Reader;
using DuckDB.NET.Data.DataChunk.Writer;
using Kurrent.Quack;
using KurrentDB.Common.Log;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using Serilog;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.SecondaryIndexing.Storage;

public class InlineFunctions(IPublisher publisher) {
	static readonly ILogger Log = Serilog.Log.Logger.ForContext("InlineFunctions");

	[Experimental("DuckDBNET001")]
	public void Init(DuckDBAdvancedConnection connection) {
		connection.RegisterScalarFunction<long, string>("kdb_get", GetEvent);
	}

	[Experimental("DuckDBNET001")]
	void GetEvent(IReadOnlyList<IDuckDBDataReader> readers, IDuckDBDataWriter writer, ulong rowCount) {
		var positions = Enumerable.Range(0, (int)rowCount).Select(x => (long)readers[0].GetValue<ulong>((ulong)x)).ToArray();
		var result = publisher.ReadEvents(positions).ToArray();

		for (ulong i = 0; i < (ulong)result.Length; i++) {
			var asString = AsDuckEvent(result[i]);
			writer.WriteValue(asString, i);
		}
	}

	static string AsDuckEvent(string stream, string eventType, DateTime created, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> meta) {
		var dataString = Helper.UTF8NoBom.GetString(data.Span);
		var metaString = meta.Length == 0 ? "{}" : Helper.UTF8NoBom.GetString(meta.Span);
		return $"{{ \"data\": {dataString}, \"metadata\": {metaString}, \"stream_id\": \"{stream}\", \"created\": \"{created:u}\", \"event_type\": \"{eventType}\" }}";
	}

	static string AsDuckEvent(ResolvedEvent evt)
		=> AsDuckEvent(evt.Event.EventStreamId, evt.Event.EventType, evt.Event.TimeStamp, evt.Event.Data, evt.Event.Metadata);
}
