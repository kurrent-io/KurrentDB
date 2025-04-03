// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.LogRecords;
using Xunit;

namespace EventStore.Core.XUnit.Tests.LogAbstraction;

// checks the abstractor wires things up properly
public class LogFormatAbstractorV2Tests :
	DirectoryPerTest<LogFormatAbstractorV2Tests>,
	IDisposable {

	private readonly DirectoryFixture<LogFormatAbstractorV2Tests> _fixture = new();
	private readonly LogFormatAbstractor<string> _sut;

	public LogFormatAbstractorV2Tests() {
		_sut = new LogV2FormatAbstractorFactory().Create(new() {
			IndexDirectory = _fixture.Directory,
			InMemory = false,
			StreamExistenceFilterSize = 1_000_000,
			StreamExistenceFilterCheckpoint = new InMemoryCheckpoint(),
		});
	}

	public void Dispose() {
		_sut.Dispose();
	}

	[Fact]
	public async Task can_init() {
		await _sut.StreamExistenceFilter.Initialize(new MockExistenceFilterInitializer("1"), 0, CancellationToken.None);
		Assert.True(_sut.StreamExistenceFilterReader.MightContain("1"));
	}

	[Fact]
	public async Task can_confirm() {
		await _sut.StreamExistenceFilter.Initialize(new MockExistenceFilterInitializer(), 0, CancellationToken.None);

		var prepare = LogRecord.SingleWrite(
			factory: _sut.RecordFactory,
			logPosition: 100,
			correlationId: Guid.NewGuid(),
			eventId: Guid.NewGuid(),
			eventStreamId: "streamA",
			expectedVersion: -1,
			eventType: "eventType",
			data: ReadOnlyMemory<byte>.Empty,
			metadata: ReadOnlyMemory<byte>.Empty);

		Assert.False(_sut.StreamExistenceFilterReader.MightContain("streamA"));

		_sut.StreamNameIndexConfirmer.Confirm(
			new[] { prepare },
			catchingUp: false,
			backend: null);

		Assert.True(_sut.StreamExistenceFilterReader.MightContain("streamA"));
	}
}
