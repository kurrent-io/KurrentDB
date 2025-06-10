using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using DuckDB.NET.Data;
using DuckDB.NET.Data.Reader;
using DuckDB.NET.Data.Writer;
using DuckDB.NET.Native;
using EventStore.Common.Log;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Duck.Infrastructure;
using EventStore.Core.Services.Transport.Common;
using Serilog;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Core.Duck;

public class InlineFunctions<TStreamId>(DuckDb db, IPublisher publisher) {
	static readonly IReadOnlyList<ColumnInfo> ColumnInfos = [
		new("stream_id", typeof(TStreamId)),
		new("event_number", typeof(long)),
		new("event_type", typeof(string)),
		new("created", typeof(DateTime)),
		new("data", typeof(string)),
		new("metadata", typeof(string)),
		new("log_position", typeof(ulong))
	];

	static readonly ILogger log = Log.Logger.ForContext("InlineFunctions");

	[Experimental("DuckDBNET001")]
	public void Run() {
		using var connection = db.OpenConnection();
		connection.RegisterTableFunction<long, long>("kdb_all", ReadAllResultCallback, ReadMapperCallback);
		connection.RegisterTableFunction<string, long>("kdb_stream", ReadStreamResultCallback, ReadMapperCallback);
		connection.RegisterScalarFunction<ulong, string>("kdb_get", GetEvent);
	}

	TableFunction ReadStreamResultCallback(IReadOnlyList<IDuckDBValueReader> parameters) {
		var stream = parameters[0].GetValue<string>();
		var startPosition = parameters[1].GetValue<long>();

		return new(ColumnInfos, ReadStream(stream, StreamRevision.FromInt64(startPosition), long.MaxValue));
	}

	TableFunction ReadAllResultCallback(IReadOnlyList<IDuckDBValueReader> parameters) {
		var startPosition = parameters.Count > 0 ? parameters[0].GetValue<long>() : 0;
		var count = parameters.Count > 1 ? parameters[1].GetValue<long>() : long.MaxValue;
		var start = startPosition == 0 ? Position.Start : new((ulong)startPosition, (ulong)startPosition);

		return new(ColumnInfos, ReadAll(start, count));
	}

	[Experimental("DuckDBNET001")]
	static void ReadMapperCallback(object item, IDuckDBDataWriter[] writers, ulong rowIndex) {
		try {
			var evt = (ResolvedEvent)item;

			writers[0].WriteValue(evt.Event.EventStreamId, rowIndex);
			writers[1].WriteValue(evt.Event.EventNumber, rowIndex);
			writers[2].WriteValue(evt.Event.EventType, rowIndex);
			writers[3].WriteValue(evt.Event.TimeStamp, rowIndex);
			writers[4].WriteValue(Encoding.UTF8.GetString(evt.Event.Data.Span), rowIndex);
			writers[5].WriteValue(evt.Event.Metadata.Length == 0 ? "{}" : Encoding.UTF8.GetString(evt.Event.Metadata.Span), rowIndex);
			writers[6].WriteValue(evt.Event.LogPosition, rowIndex);
		} catch (Exception e) {
			log.Error(e, "Error occured when mapping event {Event}", item);
			throw;
		}
	}

	IEnumerable ReadStream(string stream, StreamRevision start, long count) {
		var result = publisher.ReadStream(stream, start, count);

		foreach (var evt in result) {
			yield return evt;
		}
	}

	IEnumerable ReadAll(Position start, long count) {
		var result = publisher.ReadAll(start, count);

		foreach (var evt in result) {
			yield return evt;
		}
	}

	[Experimental("DuckDBNET001")]
	void GetEvent(IReadOnlyList<IDuckDBDataReader> readers, IDuckDBDataWriter writer, ulong rowCount) {
		var watch = new Stopwatch();
		watch.Start();

		var positions = Enumerable.Range(0, (int)rowCount).Select(x => (long)readers[0].GetValue<ulong>((ulong)x)).ToArray();
		var result = publisher.ReadEvents(positions);

		foreach (var evt in result) {
			var asString = AsDuckEvent(evt);
			writer.WriteValue(asString, (ulong)evt.Event.EventNumber);
		}

		watch.Stop();
	}

	static string AsDuckEvent(string stream, string eventType, DateTime created, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> meta) {
		var dataString = Helper.UTF8NoBom.GetString(data.Span);
		var metaString = meta.Length == 0 ? "{}" : Helper.UTF8NoBom.GetString(meta.Span);
		return $"{{ \"data\": {dataString}, \"metadata\": {metaString}, \"stream_id\": \"{stream}\", \"created\": \"{created:u}\", \"event_type\": \"{eventType}\" }}";
	}

	static string AsDuckEvent(ResolvedEvent evt)
		=> AsDuckEvent(evt.Event.EventStreamId, evt.Event.EventType, evt.Event.TimeStamp, evt.Event.Data, evt.Event.Metadata);
}
