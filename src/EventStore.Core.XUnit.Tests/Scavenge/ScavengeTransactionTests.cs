// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.InMemory;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class ScavengeTransactionTests {
	class MockTransactionFactory : ITransactionFactory<int> {
		public int BeginCount { get; private set; }
		public int CommitCount { get; private set; }
		public int RollbackCount { get; private set; }

		public int Begin() {
			return BeginCount++;
		}

		public void Commit(int transaction) {
			CommitCount++;
		}

		public void Rollback(int transaction) {
			RollbackCount++;
		}
	}

	[Fact]
	public void can_commit_then_begin() {
		var storage = new InMemoryScavengeMap<Unit, ScavengeCheckpoint>();
		var backend = new MockTransactionFactory();
		var sut = new TransactionManager<int>(backend, storage);

		var expectedCheckpoint = new ScavengeCheckpoint.Accumulating(
			new ScavengePoint(default, default, default, default),
			5);

		Assert.Equal(0, backend.BeginCount);
		Assert.Equal(0, backend.CommitCount);
		Assert.Equal(0, backend.RollbackCount);

		sut.Begin();

		Assert.Equal(1, backend.BeginCount);
		Assert.Equal(0, backend.CommitCount);
		Assert.Equal(0, backend.RollbackCount);

		sut.Commit(expectedCheckpoint);
		Assert.True(storage.TryGetValue(Unit.Instance, out var actualCheckpoint));
		Assert.Equal(expectedCheckpoint, actualCheckpoint);

		Assert.Equal(1, backend.BeginCount);
		Assert.Equal(1, backend.CommitCount);
		Assert.Equal(0, backend.RollbackCount);

		sut.Begin();

		Assert.Equal(2, backend.BeginCount);
		Assert.Equal(1, backend.CommitCount);
		Assert.Equal(0, backend.RollbackCount);
	}

	[Fact]
	public void can_rollback_then_begin() {
		var backend = new MockTransactionFactory();
		var sut = new TransactionManager<int>(
			backend,
			new InMemoryScavengeMap<Unit, ScavengeCheckpoint>());

		Assert.Equal(0, backend.BeginCount);
		Assert.Equal(0, backend.CommitCount);
		Assert.Equal(0, backend.RollbackCount);

		sut.Begin();

		Assert.Equal(1, backend.BeginCount);
		Assert.Equal(0, backend.CommitCount);
		Assert.Equal(0, backend.RollbackCount);

		sut.Rollback();

		Assert.Equal(1, backend.BeginCount);
		Assert.Equal(0, backend.CommitCount);
		Assert.Equal(1, backend.RollbackCount);

		sut.Begin();

		Assert.Equal(2, backend.BeginCount);
		Assert.Equal(0, backend.CommitCount);
		Assert.Equal(1, backend.RollbackCount);
	}

	[Fact]
	public void cannot_begin_twice() {
		var sut = new TransactionManager<int>(
			new MockTransactionFactory(),
			new InMemoryScavengeMap<Unit, ScavengeCheckpoint>());

		sut.Begin();

		Assert.Throws<InvalidOperationException>(() => {
			sut.Begin();
		});
	}

	[Fact]
	public void cannot_commit_twice() {
		var sut = new TransactionManager<int>(
			new MockTransactionFactory(),
			new InMemoryScavengeMap<Unit, ScavengeCheckpoint>());

		sut.Begin();
		sut.Commit(null);

		Assert.Throws<InvalidOperationException>(() => {
			sut.Commit(null);
		});
	}

	[Fact]
	public void cannot_commit_then_rollback() {
		var sut = new TransactionManager<int>(
			new MockTransactionFactory(),
			new InMemoryScavengeMap<Unit, ScavengeCheckpoint>());

		sut.Begin();
		sut.Commit(null);

		Assert.Throws<InvalidOperationException>(() => {
			sut.Rollback();
		});
	}

	[Fact]
	public void cannot_rollback_twice() {
		var sut = new TransactionManager<int>(
			new MockTransactionFactory(),
			new InMemoryScavengeMap<Unit, ScavengeCheckpoint>());

		sut.Begin();
		sut.Rollback();

		Assert.Throws<InvalidOperationException>(() => {
			sut.Rollback();
		});
	}

	[Fact]
	public void cannot_rollback_then_commit() {
		var sut = new TransactionManager<int>(
			new MockTransactionFactory(),
			new InMemoryScavengeMap<Unit, ScavengeCheckpoint>());

		sut.Begin();
		sut.Rollback();

		Assert.Throws<InvalidOperationException>(() => {
			sut.Commit(null);
		});
	}

	[Fact]
	public void cannot_commit_without_beginning() {
		var sut = new TransactionManager<int>(
			new MockTransactionFactory(),
			new InMemoryScavengeMap<Unit, ScavengeCheckpoint>());

		Assert.Throws<InvalidOperationException>(() => {
			sut.Commit(null);
		});
	}

	[Fact]
	public void cannot_rollback_without_beginning() {
		var sut = new TransactionManager<int>(
			new MockTransactionFactory(),
			new InMemoryScavengeMap<Unit, ScavengeCheckpoint>());

		Assert.Throws<InvalidOperationException>(() => {
			sut.Rollback();
		});
	}
}
