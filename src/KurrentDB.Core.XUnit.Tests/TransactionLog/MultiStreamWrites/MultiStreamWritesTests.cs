// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.UserManagement;
using Xunit;
using Assert = Xunit.Assert;
using OperationResult = KurrentDB.Core.Messages.OperationResult;

namespace KurrentDB.Core.XUnit.Tests.TransactionLog.MultiStreamWrites;

public class MultiStreamWritesTests(MiniNodeFixture<MultiStreamWritesTests> fixture)
	: IClassFixture<MiniNodeFixture<MultiStreamWritesTests>> {
	private static Event NewEvent => CreateEvent();
	private static Event CreateEvent(int dataSize = 4) => new(
		eventId: Guid.NewGuid(),
		eventType: "type",
		isJson: false,
		data: new string('#', dataSize),
		metadata: "metadata");

	private static Event CreateMetadataEvent(StreamMetadata metadata) => new(
		eventId: Guid.NewGuid(),
		eventType: SystemEventTypes.StreamMetadata,
		isJson: true,
		data: metadata.ToJsonBytes());

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
		Assert.Equal(0, completed.ConsistencyCheckFailures.Length);
		Assert.True(completed.PreparePosition > 0);
		Assert.Equal(completed.PreparePosition, completed.CommitPosition);

		// check we can read
		var client = new SystemClient(fixture.MiniNode.Node.MainQueue);
		var lastA = await client.Reading.ReadStreamBackwards(A, StreamRevision.End, maxCount: 1).SingleAsync();
		var lastB = await client.Reading.ReadStreamBackwards(B, StreamRevision.End, maxCount: 1).SingleAsync();
		Assert.Equal(0, lastA.OriginalEventNumber);
		Assert.Equal(0, lastB.OriginalEventNumber);

		// check we can still read after clearing the caches
		var readIndex = fixture.MiniNode.Node.ReadIndex as Core.Services.Storage.ReaderIndex.IReadIndex<string>;
		var indexBackend = readIndex.IndexReader.Backend as Core.Services.Storage.ReaderIndex.IndexBackend<string>;
		indexBackend.StreamLastEventNumberCache.Clear();
		indexBackend.StreamMetadataCache.Clear();

		lastA = await client.Reading.ReadStreamBackwards(A, StreamRevision.End, maxCount: 1).SingleAsync();
		lastB = await client.Reading.ReadStreamBackwards(B, StreamRevision.End, maxCount: 1).SingleAsync();
		Assert.Equal(0, lastA.OriginalEventNumber);
		Assert.Equal(0, lastB.OriginalEventNumber);
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

		// check we can read
		var client = new SystemClient(fixture.MiniNode.Node.MainQueue);
		var lastA = await client.Reading.ReadStreamBackwards(A, StreamRevision.End, maxCount: 1).SingleAsync();
		var lastB = await client.Reading.ReadStreamBackwards(B, StreamRevision.End, maxCount: 1).SingleAsync();
		Assert.Equal(1, lastA.OriginalEventNumber);
		Assert.Equal(0, lastB.OriginalEventNumber);

		// check we can still read after clearing the caches
		var readIndex = fixture.MiniNode.Node.ReadIndex as Core.Services.Storage.ReaderIndex.IReadIndex<string>;
		var indexBackend = readIndex.IndexReader.Backend as Core.Services.Storage.ReaderIndex.IndexBackend<string>;
		indexBackend.StreamLastEventNumberCache.Clear();
		indexBackend.StreamMetadataCache.Clear();

		lastA = await client.Reading.ReadStreamBackwards(A, StreamRevision.End, maxCount: 1).SingleAsync();
		lastB = await client.Reading.ReadStreamBackwards(B, StreamRevision.End, maxCount: 1).SingleAsync();
		Assert.Equal(1, lastA.OriginalEventNumber);
		Assert.Equal(0, lastB.OriginalEventNumber);
	}

	[Fact]
	public async Task succeeds_with_single_stream() {
		const string test = nameof(succeeds_with_single_stream);
		var A = $"{test}-a";

		var completed = await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: []);

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
			eventStreamIndexes: []);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([-1], completed.LastEventNumbers.ToArray());

		// write 3 events
		completed = await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: []);

		Assert.Equal(OperationResult.Success, completed.Result);

		// empty write to non-empty stream
		completed = await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [],
			eventStreamIndexes: []);

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
	public async Task succeeds_when_reaching_chunk_boundary() {
		const string test = nameof(succeeds_when_reaching_chunk_boundary);

		var streamA = $"{test}-a";
		var streamB = $"{test}-b";
		var streamC = $"{test}-c";

		var client = new SystemClient(fixture.MiniNode.Node.MainQueue);
		var chunkSize = fixture.MiniNode.Options.Database.ChunkSize;

		// Use up 2/3 of the remaining space in the chunk so that the next write does not fit
		var writer = fixture.MiniNode.Node.Db.Config.WriterCheckpoint.Read();
		var spaceLeftInChunk = chunkSize - (int)writer % chunkSize;
		if (spaceLeftInChunk > 10_000) {
			await client.Writing.WriteEvents(streamA, [CreateEvent(spaceLeftInChunk * 2 / 3)]);
		}

		// A write that does not fit in chunk and so creates a new one
		var thirdOfAChunk = chunkSize / 3;
		var completed = await WriteEvents(
			eventStreamIds: [streamB, streamC],
			expectedVersions: [ExpectedVersion.NoStream, ExpectedVersion.NoStream],
			events: [CreateEvent(thirdOfAChunk), CreateEvent(thirdOfAChunk)],
			eventStreamIndexes: [0, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal((writer / chunkSize) + 1, completed.CommitPosition / chunkSize); // important: wrote to the next chunk
		Assert.Equal([0, 0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([0, 0], completed.LastEventNumbers.ToArray());

		var lastA = await client.Reading.ReadStreamBackwards(streamB, StreamRevision.End, maxCount: 1).SingleAsync();
		var lastB = await client.Reading.ReadStreamBackwards(streamC, StreamRevision.End, maxCount: 1).SingleAsync();
		Assert.Equal(0, lastA.OriginalEventNumber);
		Assert.Equal(0, lastB.OriginalEventNumber);
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

		// not necessarily immediately true because the undeletes are not yet transactional with the writes
		await AssertEx.IsOrBecomesTrueAsync(async () => {
			// B is undeleted
			var read = await ReadEvents(B);
			return read.Result == ReadStreamResult.Success && read.Events.Count == 1;
		});
	}

	[Fact]
	public async Task succeeds_when_transaction_size_not_exceeded() {
		const string test = nameof(succeeds_when_transaction_size_not_exceeded);
		var A = $"{test}-a";
		var B = $"${test}-b";

		var chunkSize = fixture.MiniNode.Db.Config.ChunkSize;
		var largeEvent = new Event(Guid.NewGuid(), "type", false, new byte[chunkSize / 2]);

		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [largeEvent, NewEvent], // ~500 KiB, fits in a chunk
			eventStreamIndexes: [0, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);
	}

	[Fact]
	public async Task succeeds_with_conditional_append() {
		const string test = nameof(succeeds_with_conditional_append);
		var A = $"{test}-a";
		var B = $"{test}-b";
		var C = $"{test}-c"; // conditional stream (valid condition: NoStream)

		var completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any, ExpectedVersion.NoStream],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 1, 0]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([0, 0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([1, 0], completed.LastEventNumbers.ToArray());
	}

	[Fact]
	public async Task succeeds_with_conditional_appends() {
		const string test = nameof(succeeds_with_conditional_appends);
		var A = $"{test}-a";
		var B = $"{test}-b"; // conditional stream (valid condition: NoStream)
		var C = $"{test}-c"; // conditional stream (valid condition: NoStream)

		var completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.NoStream, ExpectedVersion.NoStream],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 0, 0]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([2], completed.LastEventNumbers.ToArray());
	}

	[Fact]
	public async Task succeeds_with_conditional_append_on_deleted_stream_and_exp_ver_any() {
		const string test = nameof(succeeds_with_conditional_append_on_deleted_stream_and_exp_ver_any);
		var A = $"{test}-a";
		var B = $"{test}-b";
		var C = $"{test}-c"; // conditional stream (hard deleted, valid condition: Any)

		await DeleteStream(C, hardDelete: true);

		var completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 1, 0]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([0, 0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([1, 0], completed.LastEventNumbers.ToArray());
	}

	[Fact]
	public async Task succeeds_with_conditional_append_on_deleted_stream_and_exp_ver_deleted_stream() {
		const string test = nameof(succeeds_with_conditional_append_on_deleted_stream_and_exp_ver_deleted_stream);
		var A = $"{test}-a";
		var B = $"{test}-b";
		var C = $"{test}-c"; // conditional stream (hard deleted, valid condition: DeletedStream)

		await DeleteStream(C, hardDelete: true);

		var completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any, EventNumber.DeletedStream],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 1, 0]);

		Assert.Equal(OperationResult.Success, completed.Result);
		Assert.Equal([0, 0], completed.FirstEventNumbers.ToArray());
		Assert.Equal([1, 0], completed.LastEventNumbers.ToArray());
	}

	[Fact]
	public async Task undeletes_stream_on_empty_write() {
		const string test = nameof(undeletes_stream_on_empty_write);
		var A = $"{test}-a";

		// write an event to A so it can be soft-deleted
		await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [NewEvent],
			eventStreamIndexes: []);

		await DeleteStream(A, hardDelete: false);

		// verify A is soft-deleted (reads as NoStream)
		var read = await ReadEvents(A);
		Assert.Equal(ReadStreamResult.NoStream, read.Result);

		// empty write to the soft-deleted stream should undelete it
		var completed = await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [],
			eventStreamIndexes: []);

		Assert.Equal(OperationResult.Success, completed.Result);

		// not necessarily immediately true because the undeletes are not yet transactional with the writes
		await AssertEx.IsOrBecomesTrueAsync(async () => {
			// A is undeleted
			read = await ReadEvents(A);
			return read.Result == ReadStreamResult.Success && read.Events is [];
		});
	}

	[Fact]
	public async Task undeletes_stream_on_metadata_write() {
		const string test = nameof(undeletes_stream_on_metadata_write);
		var A = $"{test}-a";
		var metaA = SystemStreams.MetastreamOf(A);

		// write an event to A so it can be soft-deleted
		await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [NewEvent],
			eventStreamIndexes: []);

		await DeleteStream(A, hardDelete: false);

		// verify A is soft-deleted (reads as NoStream)
		var read = await ReadEvents(A);
		Assert.Equal(ReadStreamResult.NoStream, read.Result);

		// writing a metadata record to $$A should undelete A
		var completed = await WriteEvents(
			eventStreamIds: [metaA],
			expectedVersions: [ExpectedVersion.Any],
			events: [CreateMetadataEvent(new(maxCount: 2))],
			eventStreamIndexes: []);

		Assert.Equal(OperationResult.Success, completed.Result);

		// not necessarily immediately true because the undeletes are not yet transactional with the writes
		await AssertEx.IsOrBecomesTrueAsync(async () => {
			// A is undeleted
			read = await ReadEvents(A);
			return read.Result == ReadStreamResult.Success && read.Events is [];
		});
	}

	[Fact]
	public async Task undeletes_stream_on_write_to_metadata_stream_and_stream() {
		const string test = nameof(undeletes_stream_on_write_to_metadata_stream_and_stream);
		var A = $"{test}-a";
		var metaA = SystemStreams.MetastreamOf(A);

		// write an event to A so it can be soft-deleted
		await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [NewEvent],
			eventStreamIndexes: []);

		await DeleteStream(A, hardDelete: false);

		// verify A is soft-deleted (reads as NoStream)
		var read = await ReadEvents(A);
		Assert.Equal(ReadStreamResult.NoStream, read.Result);

		// writing to both $$A and A in a single write (metadata first) should undelete A
		var completed = await WriteEvents(
			eventStreamIds: [metaA, A],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [CreateMetadataEvent(new(maxCount: 2)), NewEvent],
			eventStreamIndexes: [0, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);

		// not necessarily immediately true because the undeletes are not yet transactional with the writes
		await AssertEx.IsOrBecomesTrueAsync(async () => {
			// A is undeleted
			read = await ReadEvents(A);
			return read.Result == ReadStreamResult.Success;
		});
	}

	[Fact]
	public async Task undeletes_stream_on_write_to_stream_and_metadata_stream() {
		const string test = nameof(undeletes_stream_on_write_to_stream_and_metadata_stream);
		var A = $"{test}-a";
		var metaA = SystemStreams.MetastreamOf(A);

		// write an event to A so it can be soft-deleted
		await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [NewEvent],
			eventStreamIndexes: []);

		await DeleteStream(A, hardDelete: false);

		// verify A is soft-deleted (reads as NoStream)
		var read = await ReadEvents(A);
		Assert.Equal(ReadStreamResult.NoStream, read.Result);

		// writing to both A and $$A in a single write (stream first) should undelete A
		var completed = await WriteEvents(
			eventStreamIds: [A, metaA],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, CreateMetadataEvent(new(maxCount: 2))],
			eventStreamIndexes: [0, 1]);

		Assert.Equal(OperationResult.Success, completed.Result);

		// not necessarily immediately true because the undeletes are not yet transactional with the writes
		await AssertEx.IsOrBecomesTrueAsync(async () => {
			// A is undeleted
			read = await ReadEvents(A);
			return read.Result == ReadStreamResult.Success;
		});
	}

	[Fact]
	public async Task does_not_undelete_conditional_streams() {
		const string test = nameof(does_not_undelete_conditional_streams);
		var A = $"{test}-a";
		var B = $"{test}-b"; // conditional stream (soft deleted, no events written to it)

		// write an event to B so it can be soft-deleted
		await WriteEvents(
			eventStreamIds: [B],
			expectedVersions: [ExpectedVersion.Any],
			events: [NewEvent],
			eventStreamIndexes: []);

		await DeleteStream(B, hardDelete: false);

		// verify B is soft-deleted (reads as NoStream)
		var read = await ReadEvents(B);
		Assert.Equal(ReadStreamResult.NoStream, read.Result);

		// conditional append: write events only to A, with B as a conditional check
		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [-1, 0],
			events: [NewEvent, NewEvent],
			eventStreamIndexes: [0, 0]);

		Assert.Equal(OperationResult.Success, completed.Result);

		// B should still be soft-deleted (not undeleted as a side-effect)
		read = await ReadEvents(B);
		Assert.Equal(ReadStreamResult.NoStream, read.Result);
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
		Assert.Equal([
			new(0, 1, -1, null),
			new(2, 2, -1, null),
		], completed.ConsistencyCheckFailures.ToArray());
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
		Assert.Equal([
			new(1, ExpectedVersion.StreamExists, -1, false),
			new(2, ExpectedVersion.StreamExists, -1, false),
		], completed.ConsistencyCheckFailures.ToArray());
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
		Assert.Equal([
			new(0, -1, 1, null),
			new(1, -1, 0, null),
		], completed.ConsistencyCheckFailures.ToArray());
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

		// write A0, B0, C0, A1, B1, C1
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
		Assert.Equal([
			new(1, ExpectedVersion.Any, 1, null), // the state of stream B prevented the write.
		], completed.ConsistencyCheckFailures.ToArray());

		// events written to: A use a wrong expected version, B are idempotent, C are idempotent
		completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.NoStream, 0, 0],
			events: [eventA0, eventB0, eventC0, eventA1, eventB1, eventC1],
			eventStreamIndexes: [0, 1, 2, 0, 1, 2]);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		Assert.Equal([
			new(0, -1, 1, null),
			new(1, 0, 1, null),
			new(2, 0, 1, null),
		], completed.ConsistencyCheckFailures.ToArray());

		// events written to: A are new, B use a wrong expected version, C are idempotent
		completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [1, ExpectedVersion.NoStream, 0],
			events: [NewEvent, eventB0, eventC0, NewEvent, eventB1, eventC1],
			eventStreamIndexes: [0, 1, 2, 0, 1, 2]);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		Assert.Equal([
			new(1, -1, 1, null),
			new(2, 0, 1, null),
		], completed.ConsistencyCheckFailures.ToArray());

		// events written to: A are partly idempotent and partly new
		completed = await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.NoStream],
			events: [eventA0, NewEvent],
			eventStreamIndexes: []);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		Assert.Equal([new(0, -1, 1, null)], completed.ConsistencyCheckFailures.ToArray());
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
		Assert.Equal([new(1, ExpectedVersion.Any, EventNumber.DeletedStream, false)], completed.ConsistencyCheckFailures.ToArray());
	}

	[Fact]
	public async Task fails_when_transaction_size_is_exceeded() {
		const string test = nameof(fails_when_transaction_size_is_exceeded);
		var A = $"{test}-a";
		var B = $"${test}-b";

		var chunkSize = fixture.MiniNode.Db.Config.ChunkSize;
		var largeEvent = new Event(Guid.NewGuid(), "type", false, new byte[chunkSize]);

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

		// not all streams being written to (with eventStreamIndexes: [])
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent],
			eventStreamIndexes: [])); // equivalent to [0, 0]

		// empty write to multiple streams
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [],
			eventStreamIndexes: []));

		// empty write to multiple streams (with eventStreamIndexes: [])
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any],
			events: [],
			eventStreamIndexes: [])); // equivalent to []
	}

	[Fact]
	public async Task can_write_to_single_stream_with_eventStreamIndexes() {
		// indexes are normalized to []
		const string test = nameof(can_write_to_single_stream_with_eventStreamIndexes);
		var A = $"{test}-a";

		var completed = await WriteEvents(
			eventStreamIds: [A],
			expectedVersions: [ExpectedVersion.Any],
			events: [NewEvent, NewEvent],
			eventStreamIndexes: [0, 0]);

		Assert.Equal(OperationResult.Success, completed.Result);
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

	[Fact]
	public async Task fails_with_conditional_append_and_invalid_condition() {
		const string test = nameof(fails_with_conditional_append_and_invalid_condition);
		var A = $"{test}-a";
		var B = $"{test}-b";
		var C = $"{test}-c"; // conditional stream (invalid condition: StreamExists)

		var completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any, ExpectedVersion.StreamExists],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 1, 0]);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		Assert.Equal(0, completed.FirstEventNumbers.Length);
		Assert.Equal(0, completed.LastEventNumbers.Length);
		Assert.Equal([new(2, ExpectedVersion.StreamExists, -1, false)], completed.ConsistencyCheckFailures.ToArray());
	}

	[Fact]
	public async Task fails_with_conditional_append_on_wrongly_placed_stream() {
		const string test = nameof(fails_with_conditional_append_on_wrongly_placed_stream);
		var A = $"{test}-a"; // conditional stream (valid condition: NoStream, but should be placed at the end of the list of streams)
		var B = $"{test}-b";
		var C = $"{test}-c";

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.NoStream, ExpectedVersion.Any, ExpectedVersion.Any],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [1, 2, 1]));
	}

	[Fact]
	public async Task fails_with_conditional_appends_and_at_least_one_invalid_condition() {
		const string test = nameof(fails_with_conditional_appends_and_at_least_one_invalid_condition);
		var A = $"{test}-a";
		var B = $"{test}-b"; // conditional stream (invalid condition: StreamExists)
		var C = $"{test}-c"; // conditional stream (valid condition: NoStream)
		var D = $"{test}-d"; // conditional stream (invalid condition: StreamExists)

		var completed = await WriteEvents(
			eventStreamIds: [A, B, C, D],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.StreamExists, ExpectedVersion.NoStream, ExpectedVersion.StreamExists],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 0, 0]);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		Assert.Equal(0, completed.FirstEventNumbers.Length);
		Assert.Equal(0, completed.LastEventNumbers.Length);
		Assert.Equal([
			new(1, ExpectedVersion.StreamExists, -1, false),
			new(3, ExpectedVersion.StreamExists, -1, false),
		], completed.ConsistencyCheckFailures.ToArray());
	}

	[Fact]
	public async Task fails_with_conditional_append_and_mixed_invalid_conditions() {
		const string test = nameof(fails_with_conditional_append_and_mixed_invalid_conditions);
		var A = $"{test}-a"; // unconditional stream (invalid condition: StreamExists)
		var B = $"{test}-b"; // conditional stream (invalid condition: StreamExists)

		var completed = await WriteEvents(
			eventStreamIds: [A, B],
			expectedVersions: [ExpectedVersion.StreamExists, ExpectedVersion.StreamExists],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 0, 0]);

		Assert.Equal(OperationResult.WrongExpectedVersion, completed.Result);
		Assert.Equal(0, completed.FirstEventNumbers.Length);
		Assert.Equal(0, completed.LastEventNumbers.Length);
		Assert.Equal([
			new(0, ExpectedVersion.StreamExists, -1, false),
			new(1, ExpectedVersion.StreamExists, -1, false),
		], completed.ConsistencyCheckFailures.ToArray());
	}

	[Fact]
	public async Task fails_with_conditional_append_on_deleted_stream_and_invalid_condition() {
		const string test = nameof(fails_with_conditional_append_on_deleted_stream_and_invalid_condition);
		var A = $"{test}-a";
		var B = $"{test}-b";
		var C = $"{test}-c"; // conditional stream (hard deleted, invalid condition: StreamExists)

		await DeleteStream(C, hardDelete: true);

		var completed = await WriteEvents(
			eventStreamIds: [A, B, C],
			expectedVersions: [ExpectedVersion.Any, ExpectedVersion.Any, ExpectedVersion.StreamExists],
			events: [NewEvent, NewEvent, NewEvent],
			eventStreamIndexes: [0, 1, 0]);

		Assert.Equal(OperationResult.StreamDeleted, completed.Result);
		Assert.Equal([new(2, ExpectedVersion.StreamExists, EventNumber.DeletedStream, false)], completed.ConsistencyCheckFailures.ToArray());
	}

	public enum StreamState {
		NeverExisted,
		ExistsAtV2, // has three events
		SoftDeletedAtV2, // had 3 events, then soft-deleted
		Tombstoned,
	}

	public enum Participation {
		WriteTo,    // events are written to this stream
		CheckOnly,  // stream appears in the request only as a consistency check
	}

	// we distinguish OperationResult.WrongExpectedVersion from OperationResult.StreamDeleted only for backwards compatibility
	// the Participation.WriteTo cases all return the same OperationResult as these on v26.0.
	// the only difference between Participation.WriteTo/CheckOnly is that a tombstoned stream cannot be written to.
	public static TheoryData<long, StreamState, Participation, OperationResult> ConsistencyCheckTestCases() => new() {
		// ExpectedVersion.Any (-2): always succeeds unless the stream is hard-deleted AND we're writing to it
		{ ExpectedVersion.Any, StreamState.NeverExisted, Participation.WriteTo, OperationResult.Success },
		{ ExpectedVersion.Any, StreamState.NeverExisted, Participation.CheckOnly, OperationResult.Success },
		{ ExpectedVersion.Any, StreamState.ExistsAtV2, Participation.WriteTo, OperationResult.Success },
		{ ExpectedVersion.Any, StreamState.ExistsAtV2, Participation.CheckOnly, OperationResult.Success },
		{ ExpectedVersion.Any, StreamState.SoftDeletedAtV2, Participation.WriteTo, OperationResult.Success },
		{ ExpectedVersion.Any, StreamState.SoftDeletedAtV2, Participation.CheckOnly, OperationResult.Success },
		{ ExpectedVersion.Any, StreamState.Tombstoned, Participation.WriteTo, OperationResult.StreamDeleted }, // never write to tombstoned stream
		{ ExpectedVersion.Any, StreamState.Tombstoned, Participation.CheckOnly, OperationResult.Success }, // can check tombstoned stream

		// ExpectedVersion.StreamExists (-4): soft deleted and hard deleted do not 'exist'
		{ ExpectedVersion.StreamExists, StreamState.NeverExisted, Participation.WriteTo, OperationResult.WrongExpectedVersion },
		{ ExpectedVersion.StreamExists, StreamState.NeverExisted, Participation.CheckOnly, OperationResult.WrongExpectedVersion },
		{ ExpectedVersion.StreamExists, StreamState.ExistsAtV2, Participation.WriteTo, OperationResult.Success },
		{ ExpectedVersion.StreamExists, StreamState.ExistsAtV2, Participation.CheckOnly, OperationResult.Success },
		{ ExpectedVersion.StreamExists, StreamState.SoftDeletedAtV2, Participation.WriteTo, OperationResult.StreamDeleted },
		{ ExpectedVersion.StreamExists, StreamState.SoftDeletedAtV2, Participation.CheckOnly, OperationResult.StreamDeleted },
		{ ExpectedVersion.StreamExists, StreamState.Tombstoned, Participation.WriteTo, OperationResult.StreamDeleted },
		{ ExpectedVersion.StreamExists, StreamState.Tombstoned, Participation.CheckOnly, OperationResult.StreamDeleted },

		// ExpectedVersion.NoStream (-1): Success/Failure is inverse of EV.StreamExists
		{ ExpectedVersion.NoStream, StreamState.NeverExisted, Participation.WriteTo, OperationResult.Success },
		{ ExpectedVersion.NoStream, StreamState.NeverExisted, Participation.CheckOnly, OperationResult.Success },
		{ ExpectedVersion.NoStream, StreamState.ExistsAtV2, Participation.WriteTo, OperationResult.WrongExpectedVersion },
		{ ExpectedVersion.NoStream, StreamState.ExistsAtV2, Participation.CheckOnly, OperationResult.WrongExpectedVersion },
		{ ExpectedVersion.NoStream, StreamState.SoftDeletedAtV2, Participation.WriteTo, OperationResult.Success },
		{ ExpectedVersion.NoStream, StreamState.SoftDeletedAtV2, Participation.CheckOnly, OperationResult.Success },
		{ ExpectedVersion.NoStream, StreamState.Tombstoned, Participation.WriteTo, OperationResult.StreamDeleted }, // never write to tombstoned stream
		{ ExpectedVersion.NoStream, StreamState.Tombstoned, Participation.CheckOnly, OperationResult.Success }, // can check tombstoned stream

		// EventNumber.DeletedStream (long.MaxValue): can check stream is tombstoned (but cannot write to it)
		{ EventNumber.DeletedStream, StreamState.NeverExisted, Participation.WriteTo, OperationResult.WrongExpectedVersion },
		{ EventNumber.DeletedStream, StreamState.NeverExisted, Participation.CheckOnly, OperationResult.WrongExpectedVersion },
		{ EventNumber.DeletedStream, StreamState.ExistsAtV2, Participation.WriteTo, OperationResult.WrongExpectedVersion },
		{ EventNumber.DeletedStream, StreamState.ExistsAtV2, Participation.CheckOnly, OperationResult.WrongExpectedVersion },
		{ EventNumber.DeletedStream, StreamState.SoftDeletedAtV2, Participation.WriteTo, OperationResult.WrongExpectedVersion },
		{ EventNumber.DeletedStream, StreamState.SoftDeletedAtV2, Participation.CheckOnly, OperationResult.WrongExpectedVersion },
		{ EventNumber.DeletedStream, StreamState.Tombstoned, Participation.WriteTo, OperationResult.StreamDeleted }, // never write to tombstoned stream
		{ EventNumber.DeletedStream, StreamState.Tombstoned, Participation.CheckOnly, OperationResult.Success }, // can check tombstoned stream

		// Specific version 2: correct expected version for ExistsAtV2 & SoftDeletedAtV2
		{ 2, StreamState.NeverExisted, Participation.WriteTo, OperationResult.WrongExpectedVersion },
		{ 2, StreamState.NeverExisted, Participation.CheckOnly, OperationResult.WrongExpectedVersion },
		{ 2, StreamState.ExistsAtV2, Participation.WriteTo, OperationResult.Success },
		{ 2, StreamState.ExistsAtV2, Participation.CheckOnly, OperationResult.Success },
		{ 2, StreamState.SoftDeletedAtV2, Participation.WriteTo, OperationResult.Success },
		{ 2, StreamState.SoftDeletedAtV2, Participation.CheckOnly, OperationResult.Success },
		{ 2, StreamState.Tombstoned, Participation.WriteTo, OperationResult.StreamDeleted },
		{ 2, StreamState.Tombstoned, Participation.CheckOnly, OperationResult.StreamDeleted },

		// Specific version 5: wrong expected version for ExistsAtV2 & SoftDeletedAtV2
		{ 5, StreamState.NeverExisted, Participation.WriteTo, OperationResult.WrongExpectedVersion },
		{ 5, StreamState.NeverExisted, Participation.CheckOnly, OperationResult.WrongExpectedVersion },
		{ 5, StreamState.ExistsAtV2, Participation.WriteTo, OperationResult.WrongExpectedVersion },
		{ 5, StreamState.ExistsAtV2, Participation.CheckOnly, OperationResult.WrongExpectedVersion },
		{ 5, StreamState.SoftDeletedAtV2, Participation.WriteTo, OperationResult.WrongExpectedVersion },
		{ 5, StreamState.SoftDeletedAtV2, Participation.CheckOnly, OperationResult.WrongExpectedVersion },
		{ 5, StreamState.Tombstoned, Participation.WriteTo, OperationResult.StreamDeleted },
		{ 5, StreamState.Tombstoned, Participation.CheckOnly, OperationResult.StreamDeleted }
	};

	[Theory]
	[MemberData(nameof(ConsistencyCheckTestCases))]
	public async Task consistency_check_respects_expected_version_and_stream_state(
		long expectedVersion,
		StreamState streamState,
		Participation participation,
		OperationResult expectedResult) {

		const string test = nameof(consistency_check_respects_expected_version_and_stream_state);
		var label = $"{test}-{VersionLabel(expectedVersion)}-{streamState}-{participation}";
		var A = $"{label}-alternate"; // alternate stream is written to if we are not writing to the target stream (we have to write to _something_)
		var T = $"{label}-target"; // target stream is the target of the checks, written to or not according to the participation

		static string VersionLabel(long v) => v switch {
			ExpectedVersion.Any => "any",
			ExpectedVersion.NoStream => "nostream",
			ExpectedVersion.StreamExists => "exists",
			EventNumber.DeletedStream => "tombstoned",
			_ => v.ToString(),
		};

		// setup stream the target stream
		if (streamState is not StreamState.NeverExisted) {
			var result = await WriteEvents([T], [ExpectedVersion.Any], [NewEvent, NewEvent, NewEvent], []);
			Assert.Equal(OperationResult.Success, result.Result);
		}

		if (streamState is StreamState.SoftDeletedAtV2) {
			await DeleteStream(T, hardDelete: false);
		}

		if (streamState is StreamState.Tombstoned) {
			await DeleteStream(T, hardDelete: true);
		}

		// attempt to write to the anchor and target streams according to the participation
		var completed = participation == Participation.WriteTo
			// write to the target stream
			? await WriteEvents(
				eventStreamIds: [T],
				expectedVersions: [expectedVersion],
				events: [NewEvent],
				eventStreamIndexes: [0])
			// write to the alternate stream, target stream participation is CheckOnly
			: await WriteEvents(
				eventStreamIds: [A, T], // todo: switch to T, A when we can (and adjust expectedVersions and eventStreamIndexes)
				expectedVersions: [ExpectedVersion.Any, expectedVersion],
				events: [NewEvent],
				eventStreamIndexes: [0]);

		Assert.Equal(expectedResult, completed.Result);

		if (expectedResult == OperationResult.Success) {
			Assert.Equal(0, completed.ConsistencyCheckFailures.Length);
		} else {
			// There must be exactly one failure, and it must be for stream T (index 0)
			Assert.Equal(1, completed.ConsistencyCheckFailures.Length);
			var failure = completed.ConsistencyCheckFailures.Span[0];
			var expectedFailureIndex = participation == Participation.WriteTo ? 0 : 1;
			Assert.Equal(expectedFailureIndex, failure.StreamIndex);
			Assert.Equal(expectedVersion, failure.ExpectedVersion);
			Assert.Equal(streamState switch {
				StreamState.NeverExisted =>
					ExpectedVersion.NoStream,
				StreamState.ExistsAtV2 or StreamState.SoftDeletedAtV2 =>
					2,
				StreamState.Tombstoned =>
					EventNumber.DeletedStream,
				_ =>
					throw new ArgumentOutOfRangeException(nameof(streamState)),
			}, failure.ActualVersion);

			Assert.Equal((streamState, expectedVersion) switch {
				(StreamState.Tombstoned, _) =>
					false,
				(StreamState.SoftDeletedAtV2, ExpectedVersion.StreamExists) =>
					true,
				(_, ExpectedVersion.StreamExists) =>
					false,
				_ =>
					null,
			}, failure.IsSoftDeleted);
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
			eventStreamIndexes: eventStreamIndexes,
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
