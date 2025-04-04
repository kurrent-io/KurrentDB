// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.LogAbstraction;
using EventStore.Core.LogAbstraction.Common;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.XUnit.Tests.Helpers;
using Xunit;

namespace EventStore.Core.XUnit.Tests.LogAbstraction.Common;

public class StreamExistenceFilterTests :
	INameExistenceFilterTests,
	IAsyncLifetime,
	IClassFixture<DirectoryFixture<StreamExistenceFilterTests>> {
	private readonly DirectoryFixture<StreamExistenceFilterTests> _fixture;

	public StreamExistenceFilterTests(DirectoryFixture<StreamExistenceFilterTests> fixture) {
		_fixture = fixture;
	}

	async Task IAsyncLifetime.InitializeAsync() {
		Sut = GenSut();
		await Sut.Initialize(new MockExistenceFilterInitializer(), 0, CancellationToken.None);
	}

	Task IAsyncLifetime.DisposeAsync() {
		var task = Task.CompletedTask;
		try {
			Dispose();
		} catch (Exception e) {
			task = Task.FromException(e);
		}

		return task;
	}

	protected override INameExistenceFilter Sut { get; set; }

	private StreamExistenceFilter GenSut(
		[System.Runtime.CompilerServices.CallerMemberName] string name = "",
		TimeSpan? checkpointInterval = null,
		long size = 10_000,
		bool useHasher = true) {

		checkpointInterval ??= TimeSpan.FromMilliseconds(10);
		var checkpointPath = Path.Combine(_fixture.Directory, $"{name}.chk");
		var checkpoint = new FileCheckpoint(checkpointPath, name, initValue: -1);
		var filter = new StreamExistenceFilter(
			directory: _fixture.Directory,
			checkpoint: checkpoint,
			filterName: name,
			size: size,
			checkpointInterval: checkpointInterval.Value,
			checkpointDelay: TimeSpan.Zero,
			hasher: useHasher ? Hasher : null);
		DisposeLater(new FakeDisposable(() => checkpoint.Close(flush: true)));
		DisposeLater(filter);
		return filter;
	}

	[Fact]
	public void can_add() {
		var name = "can_add";
		Assert.False(Sut.MightContain(name));
		Sut.Add(name);
		Assert.True(Sut.MightContain(name));
		Sut.Verify(corruptionThreshold: 0);
	}

	[Fact]
	public async Task can_add_without_hasher() {
		var sut = GenSut(useHasher: false);
		await sut.Initialize(new MockExistenceFilterInitializer(), 0, CancellationToken.None);
		var name = "can_add_without_hasher";
		Assert.False(sut.MightContain(name));
		sut.Add(name);
		Assert.True(sut.MightContain(name));
		sut.Verify(corruptionThreshold: 0);
	}

	[Fact]
	public void ensures_initialized() {
		var sut = GenSut();
		Assert.Throws<InvalidOperationException>(() => {
			sut.MightContain("something");
		});
		sut.Verify(corruptionThreshold: 0);
	}

	[Fact]
	public async Task can_truncate() {
		var sut = GenSut();
		await sut.Initialize(new MockExistenceFilterInitializer("0", "1", "2"), 0, CancellationToken.None);
		Assert.Equal(2L, sut.CurrentCheckpoint);
		// wait for flush so that we have something to truncate (or it will do nothing)
		AssertEx.IsOrBecomesTrue(() => sut.CurrentCheckpointFlushed == 2, TimeSpan.FromSeconds(20));

		// truncate works
		sut.Dispose();
		sut = GenSut();
		sut.TruncateTo(1);
		Assert.Equal(1L, sut.CurrentCheckpoint);

		// even after restart
		sut.Dispose();
		sut = GenSut();
		Assert.Equal(1L, sut.CurrentCheckpoint);
	}

	[Fact]
	public async Task on_restart_checkpoint_does_not_exceed_data() {
		var sut = GenSut();
		await sut.Initialize(new MockExistenceFilterInitializer(), 0, CancellationToken.None);

		Assert.Equal(-1, sut.CurrentCheckpoint);
		Assert.False(sut.MightContain("0"));
		Assert.False(sut.MightContain("1"));

		sut.Add("0");
		sut.CurrentCheckpoint = 0;

		Assert.Equal(0, sut.CurrentCheckpoint);
		Assert.True(sut.MightContain("0"));
		Assert.False(sut.MightContain("1"));

		// wait for flush of 0
		AssertEx.IsOrBecomesTrue(() => sut.CurrentCheckpointFlushed == 0, TimeSpan.FromSeconds(20));

		sut.Add("1");
		sut.CurrentCheckpoint = 1;

		Assert.Equal(1, sut.CurrentCheckpoint);
		Assert.True(sut.MightContain("0"));
		Assert.True(sut.MightContain("1"));

		// do not wait for flush of 1

		// when restart
		sut.Dispose();
		sut = GenSut();
		await sut.Initialize(new MockExistenceFilterInitializer(), 0, CancellationToken.None);

		// then
		Assert.Equal(0, sut.CurrentCheckpoint);
		Assert.True(sut.MightContain("0"));
		// "1" will have been flushed when disposing
		// Assert.False(sut.MightContain("1"));
		sut.Verify(corruptionThreshold: 0);
	}

	[Fact]
	public async Task when_flushed_then_checkpoint_is_persisted() {
		var sut = GenSut();
		await sut.Initialize(new MockExistenceFilterInitializer("0", "1", "2"), 0, CancellationToken.None);

		// wait for flush, then close
		AssertEx.IsOrBecomesTrue(() => sut.CurrentCheckpointFlushed == 2, TimeSpan.FromSeconds(20));
		sut.Dispose();

		// reopen, checkpoint should still be the same
		sut = GenSut();
		Assert.Equal(2L, sut.CurrentCheckpoint);
		sut.Verify(corruptionThreshold: 0);
	}

	[Fact]
	public async Task when_missing_dat_then_reset_checkpoint() {
		var sut = GenSut();
		await sut.Initialize(new MockExistenceFilterInitializer("0", "1", "2"), 0, CancellationToken.None);

		// wait for flush, then close
		AssertEx.IsOrBecomesTrue(() => sut.CurrentCheckpointFlushed == 2, TimeSpan.FromSeconds(20));
		sut.Dispose();

		// delete dat file. on reopening checkpoint must be reset
		File.Delete(sut.DataFilePath);
		sut = GenSut();
		Assert.Equal(-1L, sut.CurrentCheckpoint);
		sut.Verify(corruptionThreshold: 0);
	}

	[Fact]
	public async Task when_changing_size_then_reset_checkpoint() {
		var sut = GenSut(size: 10_000);
		await sut.Initialize(new MockExistenceFilterInitializer("0", "1", "2"), 0, CancellationToken.None);

		// wait for flush, then close
		AssertEx.IsOrBecomesTrue(() => sut.CurrentCheckpointFlushed == 2, TimeSpan.FromSeconds(20));
		sut.Dispose();

		// change size. on reopening checkpoint must be reset
		sut = GenSut(size: 20_000);
		Assert.Equal(-1L, sut.CurrentCheckpoint);
		sut.Verify(corruptionThreshold: 0);
	}

	[Fact]
	public async Task writes_can_be_read_by_another_thread() {
		var sut = GenSut();
		await sut.Initialize(new MockExistenceFilterInitializer(), 0, CancellationToken.None);

		var theValue = 12345;

		var reader = new Thread(() => {
			var x = 0;
			while (!sut.MightContain($"{theValue}")) {
				x = x * 1;
			}
		});

		reader.Start();
		Thread.Sleep(100);
		sut.Add($"{theValue}");
		Assert.True(reader.Join(TimeSpan.FromSeconds(1)));
	}
}
