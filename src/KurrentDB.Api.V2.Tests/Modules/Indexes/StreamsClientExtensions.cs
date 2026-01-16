// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Client;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Core.Resilience;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Modules.Indexes;

public static class StreamsClientExtensions {
	extension(StreamsService.StreamsServiceClient self)
	{
		public ValueTask<AppendResponse> AppendEvent(string stream, string eventType, string jsonData, CancellationToken ct) =>

			self.AppendAsync(
				new() {
					ExpectedRevision = (long)ExpectedRevisionConstants.Any,
					Stream = stream,
					Records = {
						new AppendRecord() {
							RecordId = Guid.NewGuid().ToString(),
							Schema = new() {
								Name = eventType,
								Format = SchemaFormat.Json,
							},
							Data = ByteString.CopyFromUtf8(jsonData),
						}
					},
				},
				cancellationToken: ct);

		public ValueTask<AppendResponse> AppendRecord(string stream,
			string schemaName,
			SchemaFormat schemaFormat,
			ReadOnlySpan<byte> data,
			Dictionary<string, string> properties,
			CancellationToken ct) {
			var record = new AppendRecord {
				RecordId = Guid.NewGuid().ToString(),
				Schema = new() {
					Name = schemaName,
					Format = schemaFormat,
				},
				Data = ByteString.CopyFrom(data)
			};

			foreach (var (key, value) in properties)
				record.Properties.Add(key, Value.ForString(value));

			return self.AppendAsync(
				new() {
					ExpectedRevision = (long)ExpectedRevisionConstants.Any,
					Stream = stream,
					Records = {
						record
					},
				},
				cancellationToken: ct);
		}
	}
}

public static class StreamsReadClientExtensions {
	// After a user index is created by the management plane, the execution engine will
	// create the duck table and spin up a subscription and start processing events.
	// This waits until count events have been processed.
	// The resilience covers the case that the index is not created.
	public static ValueTask<EventRecord[]> WaitForIndexEvents(
		this EventStore.Client.Streams.Streams.StreamsClient self,
		string userIndexFilter,
		int count,
		CancellationToken ct) =>

		ResiliencePipelines.RetrySlow.ExecuteAsync(
			ct => self
					.SubscribeToAllFiltered(userIndexFilter, ct)
					.Take(count)
					.ToArrayAsync(ct),
			ct);
}
