// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.TransactionLog.LogRecords;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.Storage;

public class ImplicitTransactionCalculatorTests {
	private readonly ImplicitTransactionCalculator<string> _sut = new();

	[Fact]
	public void single_stream() {
		_sut.SetPrepares([
			CreatePrepare("A"),
			CreatePrepare("A"),
			CreatePrepare("A"),
		]);

		Assert.Equal(1, _sut.NumStreamsInTransaction);
		Assert.Equal([0, 0, 0], _sut.EventStreamIndexes.ToArray());
	}

	[Fact]
	public void two_streams_in_order() {
		_sut.SetPrepares([
			CreatePrepare("A"),
			CreatePrepare("B"),
		]);

		Assert.Equal(2, _sut.NumStreamsInTransaction);
		Assert.Equal([0, 1], _sut.EventStreamIndexes.ToArray());
	}

	[Fact]
	public void two_streams_in_reverse_order() {
		_sut.SetPrepares([
			CreatePrepare("B"),
			CreatePrepare("A"),
		]);

		Assert.Equal(2, _sut.NumStreamsInTransaction);
		Assert.Equal([0, 1], _sut.EventStreamIndexes.ToArray());
	}

	[Fact]
	public void interleaved_streams() {
		_sut.SetPrepares([
			CreatePrepare("A"),
			CreatePrepare("B"),
			CreatePrepare("A"),
			CreatePrepare("B"),
			CreatePrepare("C"),
		]);

		Assert.Equal(3, _sut.NumStreamsInTransaction);
		Assert.Equal([0, 1, 0, 1, 2], _sut.EventStreamIndexes.ToArray());
	}

	[Fact]
	public void many_streams_first_occurrence_determines_index() {
		_sut.SetPrepares([
			CreatePrepare("C"),
			CreatePrepare("A"),
			CreatePrepare("B"),
			CreatePrepare("A"),
		]);

		Assert.Equal(3, _sut.NumStreamsInTransaction);
		Assert.Equal([0, 1, 2, 1], _sut.EventStreamIndexes.ToArray());
	}

	[Fact]
	public void single_prepare() {
		_sut.SetPrepares([
			CreatePrepare("A"),
		]);

		Assert.Equal(1, _sut.NumStreamsInTransaction);
		Assert.Equal([0], _sut.EventStreamIndexes.ToArray());
	}

	[Fact]
	public void empty_prepares() {
		_sut.SetPrepares([]);

		Assert.Equal(0, _sut.NumStreamsInTransaction);
		Assert.Equal([], _sut.EventStreamIndexes.ToArray());
	}

	[Fact]
	public void set_prepares_clears_previous_state() {
		_sut.SetPrepares([
			CreatePrepare("A"),
			CreatePrepare("B"),
		]);

		Assert.Equal(2, _sut.NumStreamsInTransaction);
		Assert.Equal([0, 1], _sut.EventStreamIndexes.ToArray());

		_sut.SetPrepares([
			CreatePrepare("X"),
			CreatePrepare("Y"),
			CreatePrepare("Z"),
		]);

		Assert.Equal(3, _sut.NumStreamsInTransaction);
		Assert.Equal([0, 1, 2], _sut.EventStreamIndexes.ToArray());
	}

	private static PrepareLogRecord CreatePrepare(string eventStreamId) => new(
		logPosition: 0,
		correlationId: Guid.NewGuid(),
		eventId: Guid.NewGuid(),
		transactionPosition: 0,
		transactionOffset: 0,
		eventStreamId: eventStreamId,
		eventStreamIdSize: null,
		expectedVersion: -2,
		timeStamp: DateTime.Now,
		flags: PrepareFlags.Data,
		eventType: "test-type",
		eventTypeSize: null,
		data: null,
		metadata: null);
}
