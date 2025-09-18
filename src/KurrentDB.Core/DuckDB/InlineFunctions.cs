// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using DuckDB.NET.Data;
using DuckDB.NET.Data.DataChunk.Reader;
using DuckDB.NET.Data.DataChunk.Writer;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.DuckDB;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.Core.DuckDB;

public class KdbGetEventSetup(IPublisher publisher) : IDuckDBSetup {
	[Experimental("DuckDBNET001")]
	public void Execute(DuckDBConnection connection) {
		connection.RegisterScalarFunction<long, string>("kdb_get", GetEvent);
	}

	public bool OneTimeOnly => false;

	[Experimental("DuckDBNET001")]
	private void GetEvent(IReadOnlyList<IDuckDBDataReader> readers, IDuckDBDataWriter writer, ulong rowCount) {
		var positions = Enumerable.Range(0, (int)rowCount).Select(x => (long)readers[0].GetValue<ulong>((ulong)x)).ToArray();
		var result = publisher.ReadEvents(positions).ToArray();

		for (ulong i = 0; i < (ulong)result.Length; i++) {
			var asString = AsDuckEvent(result[i]);
			writer.WriteValue(asString, i);
		}
	}

	private static string AsDuckEvent(string stream,
		string eventType,
		DateTime created,
		ReadOnlyMemory<byte> data,
		ReadOnlyMemory<byte> meta) {
		var dataString = Helper.UTF8NoBom.GetString(data.Span);
		var metaString = meta.Length == 0 ? "{}" : Helper.UTF8NoBom.GetString(meta.Span);
		return
			$"{{ \"data\": {dataString}, \"metadata\": {metaString}, \"stream_id\": \"{stream}\", \"created\": \"{created:u}\", \"event_type\": \"{eventType}\" }}";
	}

	private static string AsDuckEvent(ResolvedEvent evt)
		=> AsDuckEvent(evt.Event.EventStreamId, evt.Event.EventType, evt.Event.TimeStamp, evt.Event.Data, evt.Event.Metadata);
}

file static class ReadEventsExtensions {
	public static IEnumerable<ResolvedEvent> ReadEvents(this IPublisher publisher, long[] logPositions) {
		using var enumerator = GetEnumerator();

		while (enumerator.MoveNext()) {
			if (enumerator.Current is ReadResponse.EventReceived eventReceived) {
				yield return eventReceived.Event;
			}
		}

		yield break;

		IEnumerator<ReadResponse> GetEnumerator() {
			return new Enumerator.ReadLogEventsSync(
				bus: publisher,
				logPositions: logPositions,
				user: SystemAccounts.System,
				deadline: DefaultDeadline
			);
		}
	}

	private static readonly DateTime DefaultDeadline = DateTime.UtcNow.AddYears(1);
}
