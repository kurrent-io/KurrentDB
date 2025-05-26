// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Subscriptions;

namespace KurrentDB.SecondaryIndexing.Tests.Subscriptions;

public class SecondaryIndexCommitterTests {
	[Fact]
	public async Task Commits_On_Threshold() {
		// Given
		var callCount = 0;
		var commitSignal = new ManualResetEventSlim(false);
		var batchSize = 0;

		var indexCommitter = new SecondaryIndexCommitter(5, 10000, () => {
			Interlocked.Increment(ref callCount);
			batchSize = 5;
			commitSignal.Set();
		}, CancellationToken.None);

		// When
		for (int i = 0; i < 5; i++) {
			indexCommitter.Increment();
		}

		var committed = commitSignal.Wait(CommitSignalTimeout);
		await indexCommitter.DisposeAsync();

		// Then
		Assert.True(committed);
		Assert.Equal(1, callCount);
		Assert.Equal(5, batchSize);
	}

	[Fact]
	public async Task Commits_On_Timer() {
		// Given
		var callCount = 0;
		var commitSignal = new ManualResetEventSlim(false);

		var indexCommitter = new SecondaryIndexCommitter(1000, 10, () => {
			Interlocked.Increment(ref callCount);
			commitSignal.Set();
		}, CancellationToken.None);

		// When
		indexCommitter.Increment();
		var committed = commitSignal.Wait(CommitSignalTimeout);
		await indexCommitter.DisposeAsync();

		// Then
		Assert.True(committed);
		Assert.Equal(1, callCount);
	}

	[Fact]
	public async Task Commits_MultipleTimes_On_Timer() {
		// Given
		var callCount = 0;

		SecondaryIndexCommitter indexCommitter = null!;

		var startedAt = DateTime.UtcNow;
		var elapsed = new TaskCompletionSource<TimeSpan>();
		var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
		cts.Token.Register(() => elapsed.TrySetCanceled(cts.Token));

		await using (indexCommitter = new(1000, 10, () => {
			             var commitCounts = Interlocked.Increment(ref callCount);

			             if (commitCounts == 5) {
				             elapsed.SetResult(DateTime.UtcNow - startedAt);
			             } else {
				             // ReSharper disable once AccessToModifiedClosure
				             indexCommitter.Increment();
			             }
		             }, CancellationToken.None)) {

			// When
			indexCommitter.Increment();

			var totalCommitsElapsedTimeInMs = await elapsed.Task;

			// Then
			Assert.True(callCount >= 5);
			Assert.True(totalCommitsElapsedTimeInMs >= TimeSpan.FromMilliseconds(5 * 10));
		}
	}

	[Fact]
	public async Task Commit_Is_Not_Reentrant() {
		// Given
		var commitInProgress = new ManualResetEventSlim(false);
		var commitReleaser = new ManualResetEventSlim(false);
		var maxConcurrentCommits = 0;
		var currentConcurrentCommits = 0;

		var indexCommitter = new SecondaryIndexCommitter(3, 10000, () => {
			var currentCommits = Interlocked.Increment(ref currentConcurrentCommits);
			maxConcurrentCommits = Math.Max(maxConcurrentCommits, currentCommits);
			commitInProgress.Set();
			commitReleaser.Wait();
			Interlocked.Decrement(ref currentConcurrentCommits);
		}, CancellationToken.None);

		// When
		for (int i = 0; i < 3; i++) indexCommitter.Increment();

		Assert.True(commitInProgress.Wait(CommitSignalTimeout));

		for (int i = 0; i < 3; i++) indexCommitter.Increment();

		commitReleaser.Set();

		// Then
		await indexCommitter.DisposeAsync();

		Assert.Equal(1, maxConcurrentCommits);
	}

	[Fact]
	public async Task Multiple_Threads_Cause_No_Double_Commits() {
		// Given
		var callCount = 0;
		var commitSignal = new ManualResetEventSlim(false);

		var indexCommitter = new SecondaryIndexCommitter(100, 5000, () => {
			Interlocked.Increment(ref callCount);
			commitSignal.Set();
		}, CancellationToken.None);

		// When
		var tasks = Enumerable.Range(0, 100)
			.Select(_ => Task.Run(() => indexCommitter.Increment()))
			.ToArray();

		await Task.WhenAll(tasks);

		var committed = commitSignal.Wait(CommitSignalTimeout);
		await indexCommitter.DisposeAsync();

		// Then
		Assert.True(committed);
		Assert.Equal(1, callCount);
	}

	[Fact]
	public async Task Does_Not_Commit_Empty() {
		// Given
		var callCount = 0;

		// When
		var indexCommitter = new SecondaryIndexCommitter(1000, 10, () => {
			Interlocked.Increment(ref callCount);
		}, CancellationToken.None);
		await Task.Delay(30);
		await indexCommitter.DisposeAsync();

		// Then
		Assert.Equal(0, callCount);
	}

	[Fact]
	public async Task Respects_Cancellation() {
		// Given
		var callCount = 0;
		var cts = new CancellationTokenSource();

		// When
		var indexCommitter = new SecondaryIndexCommitter(1, 1000, () => {
			Interlocked.Increment(ref callCount);
		}, cts.Token);
		await cts.CancelAsync();
		indexCommitter.Increment();

		await Task.Delay(30, CancellationToken.None);
		await indexCommitter.DisposeAsync();

		// Then
		Assert.Equal(0, callCount);
	}

	[Fact]
	public async Task Increment_After_Dispose_Throws() {
		// Given
		var indexCommitter = new SecondaryIndexCommitter(10, 100, () => { }, CancellationToken.None);

		// When
		await indexCommitter.DisposeAsync();

		// Then
		var ex = Assert.Throws<ObjectDisposedException>(() => indexCommitter.Increment());
		Assert.Contains(nameof(SecondaryIndexCommitter), ex.Message);
	}

	[Fact]
	public async Task Multiple_Dispose_Calls_Are_Safe() {
		// Given
		var disposeCalled = 0;
		var indexCommitter = new SecondaryIndexCommitter(10, 100, () => { }, CancellationToken.None);

		try {
			await indexCommitter.DisposeAsync();
			disposeCalled++;

			await indexCommitter.DisposeAsync();
			disposeCalled++;
		} catch (Exception) {
			// Exception would cause the test to fail
		}

		// Then
		Assert.Equal(2, disposeCalled);
	}

	[Fact]
	public async Task Concurrent_Dispose_Calls_Are_Safe() {
		// Given
		var indexCommitter = new SecondaryIndexCommitter(10, 100, () => { }, CancellationToken.None);
		var exceptions = 0;

		// When
		var disposeTasks = Enumerable.Range(0, 5)
			.Select(_ => Task.Run(async () => {
				try {
					await indexCommitter.DisposeAsync();
				} catch (Exception) {
					Interlocked.Increment(ref exceptions);
				}
			}))
			.ToArray();

		await Task.WhenAll(disposeTasks);

		// Then
		Assert.Equal(0, exceptions);
	}

	[Fact]
	public async Task Disposal_During_Active_Commit_Allows_Commit_To_Complete() {
		// Given
		var commitStarted = new ManualResetEventSlim(false);
		var commitBlocker = new ManualResetEventSlim(false);
		var commitCompleted = false;
		var commitTask = new TaskCompletionSource<bool>();

		var indexCommitter = new SecondaryIndexCommitter(5, 1000, () => {
			commitStarted.Set();
			commitTask.SetResult(true); // Signal that commit has started
			commitBlocker.Wait(); // Wait indefinitely (no timeout)
			commitCompleted = true;
		}, CancellationToken.None);

		// When
		var incrementTask = Task.Run(() => {
			int attemptCount = 0;
			int maxAttempts = 50;

			while (!commitStarted.IsSet && attemptCount < maxAttempts) {
				indexCommitter.Increment();
				Thread.Sleep(10);
				attemptCount++;
			}
		});

		await commitTask.Task;
		await incrementTask;

		var disposeTask = Task.Run(async () => await indexCommitter.DisposeAsync());

		commitBlocker.Set();
		await disposeTask;

		// Then
		Assert.True(commitCompleted);
	}

	private static readonly TimeSpan CommitSignalTimeout = TimeSpan.FromSeconds(5);
}
