// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Data.Redaction;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using FluentAssertions;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RedactionService;


[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class EventPositionTests<TLogFormat, TStreamId> : RedactionServiceTestFixture<TLogFormat,TStreamId> {
	private const string StreamId = nameof(EventPositionTests<TLogFormat, TStreamId>);
	private readonly Dictionary<long, List<EventPosition>> _positions = new();

	private async ValueTask WriteEvent(string streamId, long eventNumber, string data, CancellationToken token) {
		var eventRecord = await WriteSingleEvent(streamId, eventNumber, data, token: token);
		if (!_positions.ContainsKey(eventNumber))
			_positions[eventNumber] = new();

		var chunk = await Db.Manager.GetInitializedChunkFor(eventRecord.LogPosition, token);
		var eventOffset = await chunk.GetActualRawPosition(eventRecord.LogPosition, token);
		var eventPosition = new EventPosition(
			eventRecord.LogPosition, Path.GetFileName(chunk.LocalFileName), chunk.ChunkHeader.MinCompatibleVersion, chunk.IsReadOnly, (uint)eventOffset);
		_positions[eventNumber].Add(eventPosition);
	}

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		await WriteEvent(StreamId, 2, "data 2", token: token);
		await WriteEvent(StreamId, 0, "data 0", token: token);
		await WriteEvent(StreamId, 1, "data 1", token: token);
		await WriteEvent(StreamId, 2, "data 2", token: token); // duplicate
	}

	private async Task<RedactionMessage.GetEventPositionCompleted> GetEventPosition(long eventNumber) {
		var e = new TcsEnvelope<RedactionMessage.GetEventPositionCompleted>();
		await RedactionService.As<IAsyncHandle<RedactionMessage.GetEventPosition>>().HandleAsync(new RedactionMessage.GetEventPosition(e, StreamId, eventNumber), CancellationToken.None);
		return await e.Task;
	}

	[Test]
	public async Task can_get_positions_of_event_0() {
		var msg = await GetEventPosition(0);
		Assert.AreEqual(GetEventPositionResult.Success, msg.Result);
		Assert.AreEqual(1, msg.EventPositions.Length);
		msg.EventPositions.Should().BeEquivalentTo(_positions[0].ToArray());
	}

	[Test]
	public async Task can_get_positions_of_event_1() {
		var msg = await GetEventPosition(1);
		Assert.AreEqual(GetEventPositionResult.Success, msg.Result);
		Assert.AreEqual(1, msg.EventPositions.Length);
		msg.EventPositions.Should().BeEquivalentTo(_positions[1].ToArray());
	}

	[Test]
	public async Task can_get_positions_of_event_2() {
		var msg = await GetEventPosition(2);
		Assert.AreEqual(GetEventPositionResult.Success, msg.Result);
		Assert.AreEqual(2, msg.EventPositions.Length);
		msg.EventPositions.Should().BeEquivalentTo(_positions[2].ToArray());
	}
}
