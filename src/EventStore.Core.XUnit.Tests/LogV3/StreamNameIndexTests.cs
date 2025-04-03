// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.LogAbstraction.Common;
using EventStore.Core.LogV3;
using EventStore.Core.LogV3.FASTER;
using EventStore.Core.TransactionLog.LogRecords;
using Xunit;
using StreamId = System.UInt32;

namespace EventStore.Core.XUnit.Tests.LogV3;

public class StreamNameIndexTests : IDisposable {
	readonly string _outputDir = $"testoutput/{nameof(StreamNameIndexTests)}";
	FASTERNameIndexPersistence _persistence;
	NameIndex _sut;

	public StreamNameIndexTests() {
		TryDeleteDirectory();
		GenSut();
	}

	void TryDeleteDirectory() {
		try {
			Directory.Delete(_outputDir, recursive: true);
		} catch { }
	}

	void GenSut() {
		_sut?.CancelReservations();
		_persistence?.Dispose();
		_persistence = new FASTERNameIndexPersistence(
			indexName: "StreamNameIndexPersistence",
			logDir: _outputDir,
			firstValue: LogV3SystemStreams.FirstRealStream,
			valueInterval: LogV3SystemStreams.StreamInterval,
			initialReaderCount: 1,
			maxReaderCount: 1,
			enableReadCache: true,
			checkpointInterval: Timeout.InfiniteTimeSpan);

		_sut = new(
			indexName: "StreamNameIndex",
			firstValue: LogV3SystemStreams.FirstRealStream,
			valueInterval: LogV3SystemStreams.StreamInterval,
			existenceFilter: new NoNameExistenceFilter(),
			persistence: _persistence,
			metastreams: new LogV3Metastreams(),
			recordTypeToHandle: typeof(LogV3StreamRecord));
	}

	public void Dispose() {
		_persistence?.Dispose();
		TryDeleteDirectory();
	}

	[Fact]
	public void can_handle_slightly_larger_name() {
		// fails if we copy the spanbyte returned from GetKey in scan (!)
		var name = "abcdefghijklmopqrstuv";
		_persistence.Add(name, 1024);
		Assert.Collection(
			_persistence.Scan(),
			x => {
				Assert.Equal(name, x.Name);
				Assert.Equal(1024U, x.Value);
			});
	}

	[Fact]
	public void can_reserve_and_confirm() {
		// reserve streamA
		Assert.False(_sut.GetOrReserve("streamA", out var streamNumber, out var newNumber, out var newName));
		Assert.Equal("streamA", newName);
		Assert.Equal(LogV3SystemStreams.FirstRealStream, streamNumber);
		Assert.Equal(LogV3SystemStreams.FirstRealStream, newNumber);

		// can be found by writer
		Assert.True(_sut.GetOrReserve("streamA", out streamNumber, out _, out _));
		Assert.Equal(LogV3SystemStreams.FirstRealStream, streamNumber);

		// cannot be found by reader
		Assert.False(_persistence.TryGetValue("streamA", out _));

		// confirm (i.e. it has now has been replicated)
		_sut.Confirm("streamA", streamNumber);

		// can be found by writer and reader
		Assert.True(_sut.GetOrReserve("streamA", out streamNumber, out _, out _));
		Assert.Equal(LogV3SystemStreams.FirstRealStream, streamNumber);
		Assert.True(_persistence.TryGetValue("streamA", out streamNumber));
		Assert.Equal(LogV3SystemStreams.FirstRealStream, streamNumber);
	}

	[Fact]
	public void can_unsolicited_confirm() {
		_sut.Confirm("streamA", 1024);
		_sut.Confirm("streamB", 1026);
		Assert.False(_sut.GetOrReserve("streamC", out var numberC, out _, out _));
		Assert.Equal(1028U, numberC);
	}

	[Fact]
	public void can_cancel_reservations() {
		Assert.False(_sut.GetOrReserve("streamA", out var numberA, out _, out _));
		Assert.Equal(1024U, numberA);
		_sut.CancelReservations();
		Assert.False(_sut.GetOrReserve("streamC", out var numberC, out _, out _));
		Assert.Equal(numberA, numberC);
	}

	[Fact]
	public async Task can_checkpoint_log() {
		// reserve A and B
		Assert.False(_sut.GetOrReserve("streamA", out var numberA, out _, out _));
		Assert.False(_sut.GetOrReserve("streamB", out var numberB, out _, out _));

		// persist A
		_sut.Confirm("streamA", numberA);

		// checkpoint (persists to disk)
		await _persistence.CheckpointLogAsync();

		// simulate restart
		GenSut();

		// A can be found by writer and reader
		Assert.True(_sut.GetOrReserve("streamA", out var numberAAfterRecovery, out _, out _));
		Assert.Equal(numberA, numberAAfterRecovery);
		Assert.True(_persistence.TryGetValue("streamA", out numberAAfterRecovery));
		Assert.Equal(numberA, numberAAfterRecovery);

		// B cannot be found by writer or reader
		Assert.False(_sut.GetOrReserve("streamB", out var _, out _, out _));
		Assert.False(_persistence.TryGetValue("streamB", out _));
	}

	[Fact]
	public async Task can_truncate_then_catchup() {
		// put a stream into the index and persist it
		_sut.GetOrReserve("streamA", out var streamAId, out _, out _);
		await _persistence.CheckpointLogAsync();

		// call init with an empty source, removing streamA.
		await _sut.InitializeWithConfirmed(new MockNameLookup(new()), CancellationToken.None);

		// simulate restart
		GenSut();

		// when adding new stream
		_sut.GetOrReserve("streamB", out var streamBId, out _, out _);

		// expect that it has the same id as A, since that was truncated.
		Assert.Equal(streamAId, streamBId);
	}

	static readonly IEnumerable<(StreamId StreamId, string StreamName)> _streamsSource =
		Enumerable
			.Range(0, int.MaxValue)
			.Select(x => {
				var streamId = StreamIdConverter.ToStreamId(x);
				return (StreamId: streamId, StreamName: $"stream{streamId}");
			});

	// populate the sut with a given number of confirmed streams.
	void PopulateSut(int numStreams) {
		var xs = _streamsSource.Take(numStreams).ToList();

		foreach (var (streamId, streamName) in xs) {
			_sut.GetOrReserve(streamName, out var outStreamId, out var _, out var _);
			Assert.Equal(streamId, outStreamId);
			_sut.Confirm(streamName, streamId);
		}
	}

	ValueTask DeleteStreams(int numTotalStreams, int numToDelete, CancellationToken token) {
		var streamsStream = GenerateStreamsStream(numTotalStreams - numToDelete);
		var source = new MockNameLookup(streamsStream.ToDictionary(x => x.StreamId, x => x.StreamName));
		return _sut.InitializeWithConfirmed(source, token);
	}

	static IList<(StreamId StreamId, string StreamName)> GenerateStreamsStream(int numStreams) {
		return _streamsSource.Take(numStreams).ToList();
	}

	async ValueTask TestInit(int numInStreamNameIndex, int numInStandardIndex, CancellationToken token = default) {
		// given: sut populated with x streams
		PopulateSut(numInStreamNameIndex);

		// when: initialisting with source populated with y streams
		var streamsStream = GenerateStreamsStream(numInStandardIndex);
		var source = new MockNameLookup(streamsStream.ToDictionary(x => x.StreamId, x => x.StreamName));
		await _sut.InitializeWithConfirmed(source, token);

		// then: after initialisation sut should contains the same as source.
		// check that all of the streams stream is in the stream name index
		var i = 0;
		for (; i < streamsStream.Count; i++) {
			var (streamId, streamName) = streamsStream[i];
			Assert.True(_sut.GetOrReserve(streamName, out var outStreamId, out var _, out var _));
			Assert.Equal(streamId, outStreamId);
		}

		// check that the streamnameindex doesn't contain anything extra.
		foreach (var (name, value) in _persistence.Scan()) {
			var outName = await source.LookupName(value, token);
			Assert.NotEmpty(outName);
			Assert.Equal(name, outName);
		}

		// and the next created stream has the right number
		Assert.False(_sut.GetOrReserve($"{Guid.NewGuid()}", out var newStreamId, out var _, out var _));
		Assert.Equal(streamsStream.Last().StreamId + 2, newStreamId);
	}

	[Fact]
	public async Task on_init_can_catchup() {
		await TestInit(
			numInStreamNameIndex: 3000,
			numInStandardIndex: 5000);
	}

	[Fact]
	public async Task on_init_can_catchup_from_0() {
		await TestInit(
			numInStreamNameIndex: 0,
			numInStandardIndex: 5000);
	}

	[Fact]
	public async Task on_init_can_truncate() {
		await TestInit(
			numInStreamNameIndex: 5000,
			numInStandardIndex: 3000);
	}

	[Fact]
	void can_use_read_cache_for_getoradd() {
		GenSut();
		var numStreams = 100_000;
		PopulateSut(numStreams);

		void GetOrReserve() {
			Assert.True(_sut.GetOrReserve("stream2000", out var streamId, out _, out _));
			Assert.Equal(2000U, streamId);
		}

		GetOrReserve();

		var sw = new Stopwatch();
		sw.Start();
		for (int i = 0; i < 100_000; i++)
			GetOrReserve();
		Assert.True(sw.ElapsedMilliseconds < 1000);
	}

	[Fact]
	void can_use_read_cache_for_lookup() {
		GenSut();
		var numStreams = 100_000;
		PopulateSut(numStreams);

		Assert.Equal(2000U, _persistence.LookupValue("stream2000"));

		var sw = new Stopwatch();
		sw.Start();
		for (int i = 0; i < 100_000; i++)
			Assert.Equal(2000U, _persistence.LookupValue("stream2000"));
		Assert.True(sw.ElapsedMilliseconds < 1000);
	}

	[Fact]
	public void can_scan() {
		var numStreams = 5000;
		PopulateSut(numStreams);

		var scanned = _persistence.Scan().ToList();

		Assert.Equal(numStreams, scanned.Count);

		var expectedStreamId = LogV3SystemStreams.FirstRealStream;
		for (int i = 0; i < numStreams; i++) {
			Assert.Equal(expectedStreamId, scanned[i].Value);
			Assert.Equal($"stream{expectedStreamId}", scanned[i].Name);
			expectedStreamId += LogV3SystemStreams.StreamInterval;
		}
	}

	[Fact]
	public void can_scan_backwards() {
		var numStreams = 5000;
		PopulateSut(numStreams);

		var scanned = _persistence.ScanBackwards().ToList();
		scanned.Reverse();

		Assert.Equal(numStreams, scanned.Count);

		var expectedStreamId = LogV3SystemStreams.FirstRealStream;
		for (int i = 0; i < numStreams; i++) {
			Assert.Equal(expectedStreamId, scanned[i].Value);
			Assert.Equal($"stream{expectedStreamId}", scanned[i].Name);
			expectedStreamId += LogV3SystemStreams.StreamInterval;
		}
	}

	[Fact]
	public void can_scan_empty_range() {
		var numStreams = 10000;
		PopulateSut(numStreams);

		var scanned = _persistence.Scan(0, 0).ToList();
		Assert.Empty(scanned);
	}

	[Fact]
	public async Task can_scan_forwards_skipping_truncated() {
		var numStreams = 10000;
		var deletedStreams = 500;
		var remainingStreams = numStreams - deletedStreams;
		PopulateSut(numStreams);
		await DeleteStreams(numStreams, deletedStreams, CancellationToken.None);

		var scanned = _persistence.Scan().ToList();

		Assert.Equal(remainingStreams, scanned.Count);

		var expectedStreamId = LogV3SystemStreams.FirstRealStream;
		for (int i = 0; i < remainingStreams; i++) {
			Assert.Equal(expectedStreamId, scanned[i].Value);
			Assert.Equal($"stream{expectedStreamId}", scanned[i].Name);
			expectedStreamId += LogV3SystemStreams.StreamInterval;
		}
	}

	[Fact]
	public async Task can_scan_backwards_skipping_truncated() {
		var numStreams = 10000;
		var deletedStreams = 500;
		var remainingStreams = numStreams - deletedStreams;
		PopulateSut(numStreams);
		await DeleteStreams(numStreams, deletedStreams, CancellationToken.None);

		var scanned = _persistence.ScanBackwards().ToList();
		scanned.Reverse();

		Assert.Equal(remainingStreams, scanned.Count);

		var expectedStreamId = LogV3SystemStreams.FirstRealStream;
		for (int i = 0; i < remainingStreams; i++) {
			Assert.Equal(expectedStreamId, scanned[i].Value);
			Assert.Equal($"stream{expectedStreamId}", scanned[i].Name);
			expectedStreamId += LogV3SystemStreams.StreamInterval;
		}
	}

	[Fact(Skip = "slow ~10s")]
	//[Fact]
	public void can_have_multiple_readers() {
		var numStreams = 100_000;
		PopulateSut(numStreams);

		var mres = new ManualResetEventSlim();
		void RunThread() {
			try {
				var expectedStreamId = LogV3SystemStreams.FirstRealStream;
				for (int i = 0; i < numStreams; i++) {
					var streamId = _persistence.LookupValue($"stream{expectedStreamId}");
					if (streamId != expectedStreamId)
						mres.Set();
					expectedStreamId += LogV3SystemStreams.StreamInterval;
				}
			} catch {
				mres.Set();
			}
		}

		var t1 = new Thread(RunThread);
		var t2 = new Thread(RunThread);

		t1.Start();
		t2.Start();

		t1.Join();
		t2.Join();

		Assert.False(mres.IsSet);
	}
}
