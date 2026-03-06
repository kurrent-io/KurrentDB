// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using KurrentDB.Api.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Streams.AppendRecords;

static class AppendRecordsFixture {
	public static AppendRecord CreateRecord(string stream) =>
		new() {
			Stream   = stream,
			RecordId = Guid.NewGuid().ToString(),
			Schema = new SchemaInfo {
				Name   = "TestEvent.V1",
				Format = SchemaFormat.Json
			},
			Data = ByteString.CopyFromUtf8("{\"test\": true}")
		};

	public static AppendRecordsRequest SeedRequest(string stream, int count = 1) =>
		new() {
			Records = { Enumerable.Range(0, count).Select(_ => CreateRecord(stream)) }
		};

	public static async ValueTask SeedDeletedStream(ClusterVNodeTestContext fixture, string stream, int count = 1, CancellationToken ct = default) {
		await fixture.StreamsClient.AppendRecordsAsync(SeedRequest(stream, count), cancellationToken: ct);
		await fixture.SystemClient.Management.SoftDeleteStream(stream, cancellationToken: ct);
	}

	public static async ValueTask SeedTombstonedStream(ClusterVNodeTestContext fixture, string stream, int count = 1, CancellationToken ct = default) {
		await fixture.StreamsClient.AppendRecordsAsync(SeedRequest(stream, count), cancellationToken: ct);
		await fixture.SystemClient.Management.HardDeleteStream(stream, cancellationToken: ct);
	}
}
