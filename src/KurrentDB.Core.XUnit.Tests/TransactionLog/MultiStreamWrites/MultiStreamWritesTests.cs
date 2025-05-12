// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.UserManagement;
using Xunit;
using Assert = Xunit.Assert;
using OperationResult = KurrentDB.Core.Messages.OperationResult;

namespace KurrentDB.Core.XUnit.Tests.TransactionLog.MultiStreamWrites;

public class MultiStreamWritesTests(MiniNodeFixture<MultiStreamWritesTests> fixture)
	: IClassFixture<MiniNodeFixture<MultiStreamWritesTests>> {
	private static Event NewEvent => new(Guid.NewGuid(), "type", false, "data", "metadata", []);

	[Fact]
	public async Task succeeds() {
		const string test = nameof(succeeds);
		var A = $"{test}-a";
		var B = $"{test}-b";

		var correlationId = Guid.NewGuid();

		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent],
			eventStreamIndexes: [0, 1],
			correlationId);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([0, 0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([0, 0], completed.LastEventNumbers.ToArray());
		Assert.Equal(correlationId, completed.CorrelationId);
		Assert.Equal(0, completed.FailureCurrentVersions.Length);
		Assert.Equal(0, completed.FailureStreamIndexes.Length);
		Assert.True(completed.PreparePosition > 0);
		Assert.Equal(completed.PreparePosition, completed.CommitPosition);
	}

	[Fact]
	public async Task succeeds_with_aba_pattern() {
		const string test = nameof(succeeds_with_aba_pattern);
		var A = $"{test}-a";
		var B = $"{test}-b";

		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 1, 0]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([0, 0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([1, 0], completed.LastEventNumbers.ToArray());
	}

	[Fact]
	public async Task succeeds_with_single_stream() {
		const string test = nameof(succeeds_with_single_stream);
		var A = $"{test}-a";

		var completed = await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: null);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([2], completed.LastEventNumbers.ToArray());
	}

	[Fact]
	public async Task succeeds_with_empty_write_to_single_stream() {
		const string test = nameof(succeeds_with_empty_write_to_single_stream);
		var A = $"{test}-a";

		// empty write to empty stream
		var completed = await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [],
			eventStreamIndexes: null);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([-1], completed.LastEventNumbers.ToArray());

		// write 3 events
		completed = await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: null);

		Assert.Equal(OperationResult.Success, completed.Result);

		// empty write to non-empty stream
		completed = await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [],
			eventStreamIndexes: null);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([3], completed.FirstEventNumbers.ToArray());
		Assert.Equal([2], completed.LastEventNumbers.ToArray());
	}

	[Fact]
	public async Task succeeds_with_many_interleaved_streams() {
		const string test = nameof(succeeds_with_many_interleaved_streams);
		var A = $"{test}-a";
		var B = $"{test}-b";
		var C = $"{test}-c";
		var D = $"{test}-d";

		var completed = await WriteEvents(
			eventStreamIds: [A, B, C, D],
			expectedVersions: Enumerable.Repeat(ExpectedVersion.Any, 4).ToArray(),
			events: Enumerable.Range(0, 12).Select(_ => NewEvent).ToArray(),
			eventStreamIndexes: [0, 1, 0, 1, 2, 1, 3, 3, 3, 0, 2, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([0, 0, 0, 0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([2, 3, 1, 2], completed.LastEventNumbers.ToArray());
	}

	[Fact]
	public async Task succeeds_with_repeated_appends() {
		const string test = nameof(succeeds_with_repeated_appends);
		var A = $"{test}-a";
		var B = $"{test}-b";

		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 1, 0]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([0, 0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([1, 0], completed.LastEventNumbers.ToArray());

		completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 1, 0, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([2, 1], completed.FirstEventNumbers.ToArray());
		Assert.Equal([3, 2], completed.LastEventNumbers.ToArray());
	}

	[Fact]
	public async Task succeeds_when_specifying_correct_expected_version_numbers() {
		const string test = nameof(succeeds_when_specifying_correct_expected_version_numbers);
		var A = $"{test}-a";
		var B = $"{test}-b";
		var C = $"{test}-c";

		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 0, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);

		completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [1, ExpectedVersion.StreamExists, ExpectedVersion.NoStream],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 1, 2]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([2, 1, 0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([2, 1, 0], completed.LastEventNumbers.ToArray());
	}

	[Fact]
	public async Task succeeds_with_idempotent_write() {
		const string test = nameof(succeeds_with_idempotent_write);
		var A = $"{test}-a";
		var B = $"{test}-b";
		var C = $"{test}-c";

		// initial write to prefill streams
		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 1, 1, 0, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);

		var eventA = NewEvent;
		var eventB = NewEvent;
		var eventC = NewEvent;

		// new write to test idempotency
		completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any, ExpectedVersion.Any],
			events: [eventA, eventB, eventC],
			eventStreamIndexes: [0, 1, 2]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([2, 3, 0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([2, 3, 0], completed.LastEventNumbers.ToArray());

		// idempotent write with expected version any
		completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any, ExpectedVersion.Any],
			events: [eventA, eventB, eventC],
			eventStreamIndexes: [0, 1, 2]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([2, 3, 0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([2, 3, 0], completed.LastEventNumbers.ToArray());

		// idempotent write with expected version numbers
		completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [1, 2, -1],
			events: [eventA, eventB, eventC],
			eventStreamIndexes: [0, 1, 2]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([2, 3, 0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([2, 3, 0], completed.LastEventNumbers.ToArray());
	}

	[Fact]
	public async Task succeeds_when_a_stream_is_soft_deleted() {
		const string test = nameof(succeeds_when_a_stream_is_soft_deleted);
		var A = $"{test}-a";
		var B = $"{test}-b";

		// initial write to prefill streams
		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 0, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);

		await DeleteStream(B, hardDelete: false);

		completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 0, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([2, 1], completed.FirstEventNumbers.ToArray());
		Assert.Equal([3, 1], completed.LastEventNumbers.ToArray());
	}

	[Fact]
	public async Task succeeds_when_transaction_size_not_exceeded() {
		const string test = nameof(succeeds_when_transaction_size_not_exceeded);
		var A = $"{test}-a";
		var B = $"${test}-b";

		var chunkSize = fixture.MiniNode.Db.Config.ChunkSize;
		var largeEvent = new Event(Guid.NewGuid(), "type", false, new byte[chunkSize / 2], [], []);

		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [largeEvent, NewEvent], // ~500 KiB, fits in a chunk
			eventStreamIndexes: [0, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);
	}

	[Fact]
	public async Task fails_when_specifying_wrong_expected_version_number() {
		const string test = nameof(fails_when_specifying_wrong_expected_version_number);
		var A = $"{test}-a";
		var B = $"{test}-b";
		var C = $"{test}-c";

		var completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [1, ExpectedVersion.Any, 2],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 1, 2]);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		Assert.Equal(0, completed.FirstEventNumbers.Length);
		Assert.Equal(0, completed.LastEventNumbers.Length);
		Assert.Equal([0, 2], completed.FailureStreamIndexes.ToArray());
		Assert.Equal([-1, -1], completed.FailureCurrentVersions.ToArray());
	}

	[Fact]
	public async Task fails_when_specifying_wrong_expected_version_stream_exists() {
		const string test = nameof(fails_when_specifying_wrong_expected_version_stream_exists);
		var A = $"{test}-a";
		var B = $"{test}-b";
		var C = $"{test}-c";

		var completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.StreamExists, ExpectedVersion.StreamExists],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 1, 2]);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		Assert.Equal([1, 2], completed.FailureStreamIndexes.ToArray());
		Assert.Equal([-1, -1], completed.FailureCurrentVersions.ToArray());
	}

	[Fact]
	public async Task fails_when_specifying_wrong_expected_version_no_stream() {
		const string test = nameof(fails_when_specifying_wrong_expected_version_no_stream);
		var A = $"{test}-a";
		var B = $"{test}-b";

		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 0, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);

		completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.NoStream, ExpectedVersion.NoStream],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 0, 1]);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		Assert.Equal([0, 1], completed.FailureStreamIndexes.ToArray());
		Assert.Equal([1, 0], completed.FailureCurrentVersions.ToArray());
	}

	[Fact]
	public async Task fails_when_part_of_write_is_valid_and_part_of_write_is_invalid() {
		const string test = nameof(fails_when_part_of_write_is_valid_and_part_of_write_is_invalid);
		var A = $"{test}-a";
		var B = $"{test}-b";
		var C = $"{test}-c";

		var eventA0 = NewEvent;
		var eventA1 = NewEvent;
		var eventB0 = NewEvent;
		var eventB1 = NewEvent;
		var eventC0 = NewEvent;
		var eventC1 = NewEvent;

		var completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any, ExpectedVersion.Any],
			events: [eventA0, eventB0, eventC0, eventA1, eventB1, eventC1],
			eventStreamIndexes: [0, 1, 2, 0, 1, 2]);

		Assert.Equal(OperationResult.Success, completed.Result);

		// events written to: A are new, B are idempotent, C are new
		completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, eventB0, NewEvent, NewEvent, eventB1, NewEvent],
			eventStreamIndexes: [0, 1, 2, 0, 1, 2]);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		Assert.Equal(0, completed.FirstEventNumbers.Length);
		Assert.Equal(0, completed.LastEventNumbers.Length);
		Assert.Equal(0, completed.FailureStreamIndexes.Length); // an empty array means corrupted idempotency
		Assert.Equal(0, completed.FailureCurrentVersions.Length); // an empty array means corrupted idempotency

		// events written to: A use a wrong expected version, B are idempotent, C are idempotent
		completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.NoStream, 1, 1],
			events: [eventA0, eventB0, eventC0, eventA1, eventB1, eventC1],
			eventStreamIndexes: [0, 1, 2, 0, 1, 2]);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		Assert.Equal(0, completed.FailureStreamIndexes.Length); // an empty array means corrupted idempotency
		Assert.Equal(0, completed.FailureCurrentVersions.Length); // an empty array means corrupted idempotency

		// events written to: A are new, B use a wrong expected version, C are idempotent
		completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.NoStream, 1],
			events: [NewEvent, eventB0, eventC0, NewEvent, eventB1, eventC1],
			eventStreamIndexes: [0, 1, 2, 0, 1, 2]);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		Assert.Equal(0, completed.FailureStreamIndexes.Length); // an empty array means corrupted idempotency
		Assert.Equal(0, completed.FailureCurrentVersions.Length); // an empty array means corrupted idempotency

		// events written to: A are partly idempotent and partly new
		completed = await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.NoStream],
			events: [eventA0, NewEvent],
			eventStreamIndexes: null);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		// for backwards compatibility, the arrays are populated when there is a single stream
		Assert.Equal([0], completed.FailureStreamIndexes.ToArray());
		Assert.Equal([1], completed.FailureCurrentVersions.ToArray());
	}

	[Fact]
	public async Task fails_when_a_stream_is_hard_deleted() {
		const string test = nameof(fails_when_a_stream_is_hard_deleted);
		var A = $"{test}-a";
		var B = $"{test}-b";

		// initial write to prefill streams
		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 0, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);

		await DeleteStream(B, hardDelete: true);

		completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 0, 1]);

		Assert.Equal(OperationResult.StreamDeleted, completed.Result);
		Assert.Equal([1], completed.FailureStreamIndexes.ToArray());
		Assert.Equal([long.MaxValue], completed.FailureCurrentVersions.ToArray());
	}

	[Fact]
	public async Task fails_when_transaction_size_is_exceeded() {
		const string test = nameof(fails_when_transaction_size_is_exceeded);
		var A = $"{test}-a";
		var B = $"${test}-b";

		var chunkSize = fixture.MiniNode.Db.Config.ChunkSize;
		var largeEvent = new Event(Guid.NewGuid(), "type", false, new byte[chunkSize], [], []);

		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [largeEvent, NewEvent], // ~1 MiB, doesn't fit in a chunk
			eventStreamIndexes: [0, 1]);

		Assert.Equal(OperationResult.InvalidTransaction, completed.Result);
	}

	[Fact]
	public async Task throws_when_write_request_is_invalid() {
		const string test = nameof(throws_when_write_request_is_invalid);
		var A = $"{test}-a";
		var B = $"${test}-b";

		// eventStreamIds.Length != expectedVersions.Length
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any],
			events: [NewEvent, NewEvent],
			eventStreamIndexes: [0, 1]));

		// no streams specified
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [],
			expectedVersions: [],
			events: [NewEvent],
			eventStreamIndexes: [0]));

		// events.Length != eventStreamIndexes.Length
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent],
			eventStreamIndexes: [0, 1]));

		// expected version out of valid range
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, -5],
			events: [NewEvent, NewEvent],
			eventStreamIndexes: [0, 1]));

		// invalid stream ID
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [A, string.Empty],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent],
			eventStreamIndexes: [0, 1]));

		// event stream index out of range
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent],
			eventStreamIndexes: [0, 2]));

		// stream indexes not assigned correctly
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent],
			eventStreamIndexes: [1, 0]));

		// not all streams being written to
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent],
			eventStreamIndexes: [0, 0]));

		// not all streams being written to (with eventStreamIndexes: null)
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent],
			eventStreamIndexes: null)); // equivalent to [0, 0]

		// empty write to multiple streams
		await Assert.ThrowsAsync<ArgumentException>(async () => await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [],
			eventStreamIndexes: []));

		// empty write to multiple streams (with eventStreamIndexes: null)
		await Assert.ThrowsAsync<ArgumentException>(async () => await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [],
			eventStreamIndexes: null)); // equivalent to []
	}

	[Fact]
	public async Task can_write_then_read_events() {
		const string test = nameof(can_write_then_read_events);

		const int numStreams = 10;
		const int numEvents = 100;
		var streamIds = Enumerable.Range(0, numStreams).Select(i => $"{test}-{i}").ToArray();
		var events = Enumerable.Range(0, numEvents).Select(_ => NewEvent).ToArray();

		var completed = await WriteEvents(
			eventStreamIds: streamIds,
			expectedVersions: Enumerable.Repeat(ExpectedVersion.Any, numStreams).ToArray(),
			events: events,
			eventStreamIndexes: Enumerable.Range(0, numEvents).Select(i => i % numStreams).ToArray());

		Assert.Equal(OperationResult.Success, completed.Result);

		for (int streamIndex = 0; streamIndex < numStreams; streamIndex++) {
			var streamId = streamIds[streamIndex];

			var completedRead = await ReadEvents(streamId);
			Assert.Equal(ReadStreamResult.Success, completedRead.Result);

			var readEventIds = completedRead.Events.Select(x => x.Event.EventId).ToArray();

			var idx = streamIndex;
			var writtenEventIds = events
				.Where((_, i) => i % numStreams == idx)
				.Select(evt => evt.EventId)
				.ToArray();

			Assert.Equal(numEvents / numStreams, readEventIds.Length);
			Assert.Equal(readEventIds, writtenEventIds);
		}
	}

	private Task<ClientMessage.WriteEventsCompleted> WriteEvents(
		string[] eventStreamIds,
		long[] expectedVersions,
		Event[] events,
		int[] eventStreamIndexes,
		Guid? correlationId = null) {
		var tcs = new TaskCompletionSource<ClientMessage.WriteEventsCompleted>();
		var envelope = new CallbackEnvelope(m => {
			Assert.IsType<ClientMessage.WriteEventsCompleted>(m);
			tcs.SetResult((ClientMessage.WriteEventsCompleted)m);
		});

		var writeEventsMsg = new ClientMessage.WriteEvents(
			internalCorrId: Guid.NewGuid(),
			correlationId: correlationId ?? Guid.NewGuid(),
			envelope,
			requireLeader: true,
			eventStreamIds,
			expectedVersions,
			events,
			eventStreamIndexes is null ? (LowAllocReadOnlyMemory<int>?)null : eventStreamIndexes,
			user: SystemAccounts.System);

		fixture.MiniNode.Node.MainQueue.Publish(writeEventsMsg);

		return tcs.Task;
	}

	private Task<ClientMessage.ReadStreamEventsForwardCompleted> ReadEvents(string eventStreamId) {
		var tcs = new TaskCompletionSource<ClientMessage.ReadStreamEventsForwardCompleted>();
		var envelope = new CallbackEnvelope(m => {
			Assert.IsType<ClientMessage.ReadStreamEventsForwardCompleted>(m);
			tcs.SetResult((ClientMessage.ReadStreamEventsForwardCompleted)m);
		});

		var readEventsMsg = new ClientMessage.ReadStreamEventsForward(
			internalCorrId: Guid.NewGuid(),
			correlationId: Guid.NewGuid(),
			envelope,
			eventStreamId,
			fromEventNumber: 0,
			maxCount: 1000,
			resolveLinkTos: false,
			requireLeader: false,
			validationStreamVersion: null,
			user: SystemAccounts.System,
			replyOnExpired: true);

		fixture.MiniNode.Node.MainQueue.Publish(readEventsMsg);

		return tcs.Task;
	}

	private async Task DeleteStream(string eventStreamId, bool hardDelete) {
		var tcs = new TaskCompletionSource<ClientMessage.DeleteStreamCompleted>();
		var envelope = new CallbackEnvelope(m => {
			Assert.IsType<ClientMessage.DeleteStreamCompleted>(m);
			tcs.SetResult((ClientMessage.DeleteStreamCompleted)m);
		});

		var deleteStreamMsg = new ClientMessage.DeleteStream(
			internalCorrId: Guid.NewGuid(),
			correlationId: Guid.NewGuid(),
			envelope,
			requireLeader: true,
			eventStreamId,
			ExpectedVersion.Any,
			hardDelete: hardDelete,
			SystemAccounts.System);

		fixture.MiniNode.Node.MainQueue.Publish(deleteStreamMsg);

		var completed = await tcs.Task;
		Assert.Equal(OperationResult.Success, completed.Result);
	}
}
