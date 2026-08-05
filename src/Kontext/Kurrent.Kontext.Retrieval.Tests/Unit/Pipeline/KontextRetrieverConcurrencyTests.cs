// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Pipeline;

/// <summary>
/// <see cref="KontextRetriever.RetrieveAsync"/> fans the searches out via <c>Task.WhenAll</c> and
/// forwards one <see cref="CancellationToken"/> to the planner, every search, and every stage.
/// None of that is exercised by shape-only doubles that just hand back a configured result, so
/// these tests use handshaking <see cref="TaskCompletionSource"/> doubles to pin the actual
/// concurrency behavior: genuine parallelism, token propagation, fail-loud-over-partial-recall,
/// and fusion staying correct when completion order does not match search order.
/// </summary>
[Category("Pipeline")]
public class KontextRetrieverConcurrencyTests {
	[Test]
	public async ValueTask searches_run_concurrently_not_sequentially() {
		var vectorEntered  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var keywordEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var vector  = new HandshakeSearch(RetrievalSources.Vector, vectorEntered, keywordEntered.Task, Fixtures.Candidate("a", 0.9));
		var keyword = new HandshakeSearch(RetrievalSources.Keyword, keywordEntered, vectorEntered.Task, Fixtures.Candidate("b", 0.8));

		var retriever = KontextRetriever.New().AddSearch(vector).AddSearch(keyword).Build();

		// If the searches ran one at a time, the second would never start and each side's await of
		// the other's TCS would hang forever, so bound it: a regression fails fast instead of
		// hanging the suite.
		var retrieval = retriever.RetrieveAsync(new() { Text = "query" }).AsTask();
		var raced     = await Task.WhenAny(retrieval, Task.Delay(TimeSpan.FromSeconds(2)));

		var ranConcurrently = raced == retrieval;

		await Assert.That(ranConcurrently).IsTrue()
			.Because("both searches wait on each other's handshake signal, so retrieval only completes if they were both in flight at once");
	}

	[Test]
	public async ValueTask cancellation_token_reaches_every_search() {
		using var cts = new CancellationTokenSource();

		var vector  = new TokenCapturingSearch(RetrievalSources.Vector, Fixtures.Candidate("a", 0.9));
		var keyword = new TokenCapturingSearch(RetrievalSources.Keyword, Fixtures.Candidate("b", 0.8));

		var retriever = KontextRetriever.New().AddSearch(vector).AddSearch(keyword).Build();

		await retriever.RetrieveAsync(new() { Text = "query" }, cts.Token);

		await Assert.That(vector.SeenToken).IsEqualTo(cts.Token);
		await Assert.That(keyword.SeenToken).IsEqualTo(cts.Token);
	}

	[Test]
	public async ValueTask cancellation_token_reaches_planner_and_stages() {
		using var cts = new CancellationTokenSource();

		var planner = new TokenCapturingPlanner();
		var stage   = new TokenCapturingStage();

		var retriever = KontextRetriever.New()
			.Planner(planner)
			.AddSearch(new FakeSearch(RetrievalSources.Vector, Fixtures.Candidate("a", 0.9)))
			.AddStage(stage)
			.Build();

		await retriever.RetrieveAsync(new() { Text = "query" }, cts.Token);

		await Assert.That(planner.SeenToken).IsEqualTo(cts.Token);
		await Assert.That(stage.SeenToken).IsEqualTo(cts.Token);
	}

	[Test]
	public async ValueTask pre_cancelled_token_throws_instead_of_returning_partial_results() {
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		// The vector leg would happily return "a"; the keyword leg actually honours the token and
		// never completes on its own. If cancellation were swallowed, retrieval would return the
		// vector-only pool instead of throwing.
		var retriever = KontextRetriever.New()
			.AddSearch(new FakeSearch(RetrievalSources.Vector, Fixtures.Candidate("a", 0.9)))
			.AddSearch(new CancellationHonoringSearch(RetrievalSources.Keyword))
			.Build();

		await Assert.That(async () => await retriever.RetrieveAsync(new() { Text = "query" }, cts.Token))
			.Throws<TaskCanceledException>();
	}

	[Test]
	public async ValueTask slow_search_does_not_block_faster_search_from_completing() {
		var slowGate = new TaskCompletionSource();
		var fastDone = new TaskCompletionSource();

		var fast = new GatedSearch(RetrievalSources.Vector, Task.CompletedTask, Fixtures.Candidate("fast", 1.0), finished: fastDone);
		var slow = new GatedSearch(RetrievalSources.Keyword, slowGate.Task, Fixtures.Candidate("slow", 1.0));

		var retriever = KontextRetriever.New().AddSearch(fast).AddSearch(slow).Build();

		var retrieval = retriever.RetrieveAsync(new() { Text = "query" }).AsTask();

		await fastDone.Task;

		// The slow search is still gated shut, so the whole retrieval cannot have finished yet,
		// proof the fast search's own completion did not wait on the slow one.
		await Assert.That(retrieval.IsCompleted).IsFalse();

		slowGate.SetResult();
		await retrieval;
	}

	[Test]
	public async ValueTask throwing_search_fails_retrieval_even_while_another_search_is_still_running() {
		var thrown       = new TaskCompletionSource();
		var survivorGate = new TaskCompletionSource();

		var survivor = new GatedSearch(RetrievalSources.Vector, survivorGate.Task, Fixtures.Candidate("a", 0.9));
		var throwing = new SignalingThrowingSearch(RetrievalSources.Keyword, thrown);

		var retriever = KontextRetriever.New().AddSearch(survivor).AddSearch(throwing).Build();

		var retrieval = retriever.RetrieveAsync(new() { Text = "query" }).AsTask();

		await thrown.Task;

		// The throw already happened, but the survivor is still gated shut, so retrieval must
		// still be pending. Task.WhenAll does not fault early on the first faulted task.
		await Assert.That(retrieval.IsCompleted).IsFalse();

		survivorGate.SetResult();

		// Once the survivor finishes too, the failure must still win, not the survivor's "a".
		await Assert.That(async () => await retrieval).Throws<InvalidOperationException>();
	}

	[Test]
	public async ValueTask fuses_in_input_order_regardless_of_completion_order() {
		var vectorGate = new TaskCompletionSource();

		// Vector is added first but is the one gated shut; keyword is added second and resolves
		// synchronously, so keyword completes first in real time.
		var vector  = new GatedSearch(RetrievalSources.Vector, vectorGate.Task, Fixtures.Candidate("both", 0.9));
		var keyword = new FakeSearch(RetrievalSources.Keyword, Fixtures.Candidate("both", 12.0));

		var retriever = KontextRetriever.New().AddSearch(vector).AddSearch(keyword).Build();

		var retrieval = retriever.RetrieveAsync(new() { Text = "query" }).AsTask();
		vectorGate.SetResult();

		var result = await retrieval;

		// both = 1/61 + 1/61, same RRF math as ReciprocalRankFuserTests.fuses_ranks_across_legs.
		// Completion order must not scramble which source each rank/score is attributed to.
		await Assert.That(result[0].Score).IsEqualTo(2.0 / 61).Within(1e-12);
		await Assert.That(result[0].Breakdown.SourceRanks[RetrievalSources.Vector]).IsEqualTo(1);
		await Assert.That(result[0].Breakdown.SourceRanks[RetrievalSources.Keyword]).IsEqualTo(1);
		await Assert.That(result[0].Breakdown.SourceScores[RetrievalSources.Vector]).IsEqualTo(0.9);
		await Assert.That(result[0].Breakdown.SourceScores[RetrievalSources.Keyword]).IsEqualTo(12.0);
	}
}

/// <summary>Signals it entered <see cref="SearchAsync"/>, then waits for the partner's own signal; deadlocks if run sequentially.</summary>
sealed class HandshakeSearch(string name, TaskCompletionSource entered, Task waitForPartner, SearchCandidate candidate) : ISearch {
	public string Name => name;

	public async ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) {
		entered.TrySetResult();
		await waitForPartner;
		return new CandidateSet(name, [candidate]);
	}
}

/// <summary>Records the token it was handed without otherwise doing anything interesting.</summary>
sealed class TokenCapturingSearch(string name, SearchCandidate candidate) : ISearch {
	public string Name => name;

	public CancellationToken SeenToken { get; private set; }

	public ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) {
		SeenToken = ct;
		return ValueTask.FromResult(new CandidateSet(name, [candidate]));
	}
}

sealed class TokenCapturingPlanner : IQueryPlanner {
	public CancellationToken SeenToken { get; private set; }

	public ValueTask<PlannedQuery> PlanAsync(RetrievalQuery query, CancellationToken ct = default) {
		SeenToken = ct;
		return ValueTask.FromResult(new PlannedQuery {
			Text     = query.Text,
			Tags     = query.Tags,
			Limit    = query.Limit,
			PoolSize = 60,
			AsOf     = Fixtures.Now,
		});
	}
}

sealed class TokenCapturingStage : IRetrievalStage {
	public CancellationToken SeenToken { get; private set; }

	public ValueTask<IReadOnlyList<ScoredMemory>> ProcessAsync(PlannedQuery query, IReadOnlyList<ScoredMemory> pool, CancellationToken ct = default) {
		SeenToken = ct;
		return ValueTask.FromResult(pool);
	}
}

/// <summary>Never completes on its own; only a cancelled token can end it, proving the pipeline actually forwards cancellation.</summary>
sealed class CancellationHonoringSearch(string name) : ISearch {
	public string Name => name;

	public async ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) {
		await Task.Delay(Timeout.Infinite, ct);
		return new CandidateSet(name, []);
	}
}

/// <summary>Signals when it starts and/or finishes, and does not proceed past <paramref name="gate"/> until the test releases it.</summary>
sealed class GatedSearch(string name, Task gate, SearchCandidate candidate, TaskCompletionSource? started = null, TaskCompletionSource? finished = null) : ISearch {
	public string Name => name;

	public async ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) {
		started?.TrySetResult();
		await gate;
		var set = new CandidateSet(name, [candidate]);
		finished?.TrySetResult();
		return set;
	}
}

/// <summary>Signals right before throwing, so a test can observe the failure without waiting for the whole retrieval to settle.</summary>
sealed class SignalingThrowingSearch(string name, TaskCompletionSource thrown) : ISearch {
	public string Name => name;

	public async ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) {
		await Task.Yield();
		thrown.TrySetResult();
		throw new InvalidOperationException($"The '{name}' leg is down.");
	}
}
