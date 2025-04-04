// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using EventStore.Core.LogAbstraction.Common;
using EventStore.Core.LogV3;
using EventStore.Core.LogV3.FASTER;
using EventStore.Core.TransactionLog.LogRecords;
using Xunit;

namespace EventStore.Core.XUnit.Tests.LogV3;

public class EventTypeIndexTests : IDisposable {
	readonly string _outputDir = $"testoutput/{nameof(EventTypeIndexTests)}";
	FASTERNameIndexPersistence _persistence;
	NameIndex _sut;

	public EventTypeIndexTests() {
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
			indexName: "EventTypeIndexPersistence",
			logDir: _outputDir,
			firstValue: LogV3SystemEventTypes.FirstRealEventTypeNumber,
			valueInterval: LogV3SystemEventTypes.EventTypeInterval,
			initialReaderCount: 1,
			maxReaderCount: 1,
			enableReadCache: true,
			checkpointInterval: Timeout.InfiniteTimeSpan);

		_sut = new(
			indexName: "EventTypeIndex",
			firstValue: LogV3SystemEventTypes.FirstRealEventTypeNumber,
			valueInterval: LogV3SystemEventTypes.EventTypeInterval,
			existenceFilter: new NoNameExistenceFilter(),
			persistence: _persistence,
			metastreams: null,
			recordTypeToHandle: typeof(LogV3EventTypeRecord));
	}

	public void Dispose() {
		_persistence?.Dispose();
		TryDeleteDirectory();
	}

	[Fact]
	public void uses_correct_interval() {
		Assert.False(_sut.GetOrReserve("eventTypeA", out var eventTypeNumberA, out var newNumberA, out var newNameA));
		Assert.Equal("eventTypeA", newNameA);
		Assert.Equal(LogV3SystemEventTypes.FirstRealEventTypeNumber, eventTypeNumberA);
		Assert.Equal(LogV3SystemEventTypes.FirstRealEventTypeNumber, newNumberA);

		Assert.False(_sut.GetOrReserve("eventTypeB", out var eventTypeNumberB, out var newNumberB, out var newNameB));
		Assert.Equal("eventTypeB", newNameB);
		Assert.Equal(LogV3SystemEventTypes.FirstRealEventTypeNumber + 1, eventTypeNumberB);
		Assert.Equal(LogV3SystemEventTypes.FirstRealEventTypeNumber + 1, newNumberB);
	}
}
