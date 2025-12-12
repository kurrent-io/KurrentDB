// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Client;
using Google.Protobuf;
using KurrentDB.Core.Resilience;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Modules.CustomIndexes;

public static class StreamsClientExtensions {
	public static ValueTask<AppendResponse> AppendEvent(
		this StreamsService.StreamsServiceClient self,
		string stream, string eventType, string jsonData, CancellationToken ct) =>

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
}

public static class StreamsReadClientExtensions {
	// After a custom index is created by the management plane, the execution engine will
	// create the duck table and spin up a subscription and start processing events.
	// This waits until count events have been processed.
	// The resilience covers the case that the index is not created.
	public static ValueTask<EventRecord[]> WaitForCustomIndexEvents(
		this EventStore.Client.Streams.Streams.StreamsClient self,
		string customIndexFilter,
		int count,
		CancellationToken ct) =>

		ResiliencePipelines.RetrySlow.ExecuteAsync(
			ct => self
					.SubscribeToAllFiltered(customIndexFilter, ct)
					.Take(count)
					.ToArrayAsync(ct),
			ct);
}
