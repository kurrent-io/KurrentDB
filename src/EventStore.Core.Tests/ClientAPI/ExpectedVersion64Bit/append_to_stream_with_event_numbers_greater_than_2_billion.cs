// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.ExpectedVersion64Bit;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
[Category("ClientAPI"), Category("LongRunning")]
public class append_to_stream_with_event_numbers_greater_than_2_billion<TLogFormat, TStreamId>
	: MiniNodeWithExistingRecords<TLogFormat, TStreamId> {
	private const string StreamName = "append_to_stream_with_event_numbers_greater_than_2_billion";
	private const long intMaxValue = int.MaxValue;

	public override async ValueTask WriteTestScenario(CancellationToken token) {
		await WriteSingleEvent(StreamName, intMaxValue + 1, new string('.', 3000), token: token);
		await WriteSingleEvent(StreamName, intMaxValue + 2, new string('.', 3000), token: token);
		await WriteSingleEvent(StreamName, intMaxValue + 3, new string('.', 3000), token: token);
		await WriteSingleEvent(StreamName, intMaxValue + 4, new string('.', 3000), token: token);
		await WriteSingleEvent(StreamName, intMaxValue + 5, new string('.', 3000), token: token);
	}

	public override async Task Given() {
		_store = BuildConnection(Node);
		await _store.ConnectAsync();
		await _store.SetStreamMetadataAsync(StreamName, EventStore.ClientAPI.ExpectedVersion.Any,
			EventStore.ClientAPI.StreamMetadata.Create(truncateBefore: intMaxValue + 1));
	}

	[Test]
	public async Task should_be_able_to_append_to_stream() {
		var evnt = new EventData(Guid.NewGuid(), "EventType", false, new byte[10], new byte[15]);
		var writeResult = await _store.AppendToStreamAsync(StreamName, intMaxValue + 5, evnt);
		Assert.AreEqual(intMaxValue + 6, writeResult.NextExpectedVersion);

		var readResult = await _store
			.ReadStreamEventsForwardAsync(StreamName, intMaxValue + 6, 1, false, DefaultData.AdminCredentials);
		Assert.AreEqual(SliceReadStatus.Success, readResult.Status);
		Assert.AreEqual(evnt.EventId, readResult.Events[0].Event.EventId);
	}

	[Test]
	public async Task should_throw_wrong_expected_version_when_version_incorrect() {
		var evnt = new EventData(Guid.NewGuid(), "EventType", false, new byte[10], new byte[15]);
		await AssertEx.ThrowsAsync<WrongExpectedVersionException>(
			() => _store.AppendToStreamAsync(StreamName, intMaxValue + 15, evnt));
	}
}
