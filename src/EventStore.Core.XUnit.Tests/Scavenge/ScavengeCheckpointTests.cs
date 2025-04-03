// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.TransactionLog.Scavenging;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class ScavengeCheckpointTests {
	private T RoundTrip<T>(T input, string expectedJson) where T : ScavengeCheckpoint {
		var json = ScavengeCheckpointJsonPersistence<string>.Serialize(input);
		Assert.True(ScavengeCheckpointJsonPersistence<string>.TryDeserialize(
			json,
			out var deserialized));
		Assert.Equal(expectedJson, json);
		Assert.Equal(input.ScavengePoint.Position, deserialized.ScavengePoint.Position);
		Assert.Equal(input.ScavengePoint.EffectiveNow, deserialized.ScavengePoint.EffectiveNow);
		return Assert.IsType<T>(deserialized);
	}

	private readonly ScavengePoint _scavengePoint = new ScavengePoint(
		position: 1234,
		eventNumber: 5,
		effectiveNow: EffectiveNow,
		threshold: 567);

	[Theory]
	[InlineData(null, @"{""scavengePoint"":{""position"":1234,""eventNumber"":5,""effectiveNow"":""2022-01-05T00:00:00"",""threshold"":567},""schemaVersion"":""V0"",""checkpointStage"":""Accumulating""}")]
	[InlineData(5, @"{""scavengePoint"":{""position"":1234,""eventNumber"":5,""effectiveNow"":""2022-01-05T00:00:00"",""threshold"":567},""schemaVersion"":""V0"",""checkpointStage"":""Accumulating"",""doneLogicalChunkNumber"":5}")]
	public void can_round_trip_accumulating(int? x, string expectedJson) {
		var cp = RoundTrip(new ScavengeCheckpoint.Accumulating(_scavengePoint, x), expectedJson);
		Assert.Equal(x, cp.DoneLogicalChunkNumber);
	}

	[Fact]
	public void can_round_trip_calculating_default() {
		var cp = RoundTrip(
			new ScavengeCheckpoint.Calculating<string>(_scavengePoint, default),
			@"{""scavengePoint"":{""position"":1234,""eventNumber"":5,""effectiveNow"":""2022-01-05T00:00:00"",""threshold"":567},""schemaVersion"":""V0"",""checkpointStage"":""Calculating""}");
		Assert.Equal(default, cp.DoneStreamHandle);
	}

	[Fact]
	public void can_round_trip_calculating_streamid() {
		var cp = RoundTrip(
			new ScavengeCheckpoint.Calculating<string>(_scavengePoint, StreamHandle.ForStreamId("stream1")),
			@"{""scavengePoint"":{""position"":1234,""eventNumber"":5,""effectiveNow"":""2022-01-05T00:00:00"",""threshold"":567},""schemaVersion"":""V0"",""checkpointStage"":""Calculating""," + 
			@"""doneStreamHandle"":{""kind"":""Id"",""streamId"":""stream1""}}");
		Assert.Equal("Id: stream1", cp.DoneStreamHandle.ToString());
	}

	[Fact]
	public void can_round_trip_calculating_hash() {
		var cp = RoundTrip(
			new ScavengeCheckpoint.Calculating<string>(_scavengePoint, StreamHandle.ForHash<string>(97)),
			@"{""scavengePoint"":{""position"":1234,""eventNumber"":5,""effectiveNow"":""2022-01-05T00:00:00"",""threshold"":567},""schemaVersion"":""V0"",""checkpointStage"":""Calculating""," +
			@"""doneStreamHandle"":{""kind"":""Hash"",""streamHash"":97}}");
		Assert.Equal("Hash: 97", cp.DoneStreamHandle.ToString());
	}

	[Theory]
	[InlineData(null, @"{""scavengePoint"":{""position"":1234,""eventNumber"":5,""effectiveNow"":""2022-01-05T00:00:00"",""threshold"":567},""schemaVersion"":""V0"",""checkpointStage"":""ExecutingChunks""}")]
	[InlineData(5, @"{""scavengePoint"":{""position"":1234,""eventNumber"":5,""effectiveNow"":""2022-01-05T00:00:00"",""threshold"":567},""schemaVersion"":""V0"",""checkpointStage"":""ExecutingChunks"",""doneLogicalChunkNumber"":5}")]
	public void can_round_trip_executing_chunks(int? x, string expectedJson) {
		var cp = RoundTrip(new ScavengeCheckpoint.ExecutingChunks(_scavengePoint, x), expectedJson);
		Assert.Equal(x, cp.DoneLogicalChunkNumber);
	}

	[Fact]
	public void can_round_trip_merging_chunks() {
		var cp = RoundTrip(
			new ScavengeCheckpoint.MergingChunks(_scavengePoint),
			@"{""scavengePoint"":{""position"":1234,""eventNumber"":5,""effectiveNow"":""2022-01-05T00:00:00"",""threshold"":567},""schemaVersion"":""V0"",""checkpointStage"":""MergingChunks""}");
	}

	[Fact]
	public void can_round_trip_executing_index() {
		var cp = RoundTrip(
			new ScavengeCheckpoint.ExecutingIndex(_scavengePoint),
			@"{""scavengePoint"":{""position"":1234,""eventNumber"":5,""effectiveNow"":""2022-01-05T00:00:00"",""threshold"":567},""schemaVersion"":""V0"",""checkpointStage"":""ExecutingIndex""}");
	}

	[Fact]
	public void can_round_trip_cleaning() {
		var cp = RoundTrip(
			new ScavengeCheckpoint.Cleaning(_scavengePoint),
			@"{""scavengePoint"":{""position"":1234,""eventNumber"":5,""effectiveNow"":""2022-01-05T00:00:00"",""threshold"":567},""schemaVersion"":""V0"",""checkpointStage"":""Cleaning""}");
	}

	[Fact]
	public void can_round_trip_done() {
		var cp = RoundTrip(
			new ScavengeCheckpoint.Done(_scavengePoint),
			@"{""scavengePoint"":{""position"":1234,""eventNumber"":5,""effectiveNow"":""2022-01-05T00:00:00"",""threshold"":567},""schemaVersion"":""V0"",""checkpointStage"":""Done""}");
	}
}
