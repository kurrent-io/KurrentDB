// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.Http;
using Kurrent.Kontext.Mcp.Model;
using Kurrent.Kontext.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using TUnit.Core.Exceptions;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// End-to-end behavioural tests for <see cref="KontextStore"/> against a REAL DuckDB + Lance engine and a
/// REAL ONNX Bert embedding generator (no fakes — the store never fabricates embeddings, so neither do its
/// tests). The model (<c>TaylorAI/bge-micro-v2</c>, 384-dim) is cached under
/// <c>~/.cache/ducklance-tests/bge-micro-v2/</c>; when it is neither cached nor downloadable each test skips
/// rather than fails. Runs serially: the tests share one native ONNX session and stress the same native
/// DuckDB/Lance stack, so <see cref="NotInParallelAttribute"/> keeps them deterministic.
/// </summary>
[NotInParallel]
public class KontextStoreTests {
	const int Dimensions = 384;

	static Tag Work(string value) => new() { Scope = "work", Value = value };
	static Tag Team(string value) => new() { Scope = "team", Value = value };

	// (a) --------------------------------------------------------------------------------------------

	[Test]
	public async ValueTask retain_then_get_round_trips_every_field() {
		var embedder = await OnnxEmbeddingModel.RequireAsync();
		using var dir = new TempStore();
		using var store = new KontextStore(dir.Path, embedder, Dimensions);
		await store.EnsureCreatedAsync();

		var validFrom = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
		var validTo = validFrom.AddDays(30);
		var before = DateTimeOffset.UtcNow;

		var memory = new Memory {
			Type = MemoryType.Fact,
			Content = "Sergio prefers hybrid search over pure vector search for memory recall",
			Importance = MemoryImportance.High,
			Sentiment = MemorySentiment.Positive,
			Urgency = MemoryUrgency.High,
			Tags = [Work("preferences"), Team("kontext")],
			Validity = new TemporalContext { From = validFrom, To = validTo },
			Evidence = new Evidence {
				Reasoning = "Sergio said 'always use hybrid search in the examples'",
				Citations = [new Citation.ToMemory { Memory = new MemoryRef { Id = "mem-source-1", Position = 7 } }],
			},
		};

		var retained = await store.RetainAsync(memory);
		var got = await store.GetAsync(retained.MemoryId);

		await Assert.That(got).IsNotNull();
		await Assert.That(got!.MemoryId).IsEqualTo(retained.MemoryId);
		await Assert.That(got.Content).IsEqualTo(memory.Content);

		// Enums round-trip through their SMALLINT storage.
		await Assert.That(got.MemoryType).IsEqualTo(MemoryType.Fact);
		await Assert.That(got.Importance).IsEqualTo(MemoryImportance.High);
		await Assert.That(got.Sentiment).IsEqualTo(MemorySentiment.Positive);
		await Assert.That(got.Urgency).IsEqualTo(MemoryUrgency.High);

		// Scoped tags survive exactly (scope + value), both of them.
		await Assert.That(got.Tags.Count).IsEqualTo(2);
		await Assert.That(got.Tags.Any(t => t is { Scope: "work", Value: "preferences" })).IsTrue();
		await Assert.That(got.Tags.Any(t => t is { Scope: "team", Value: "kontext" })).IsTrue();

		// Evidence round-trips as JSON, including the polymorphic citation arm.
		await Assert.That(got.Evidence).IsNotNull();
		await Assert.That(got.Evidence!.Reasoning).IsEqualTo(memory.Evidence!.Reasoning);
		await Assert.That(got.Evidence.Citations.Count).IsEqualTo(1);
		await Assert.That(got.Evidence.Citations[0] is Citation.ToMemory { Memory.Id: "mem-source-1", Memory.Position: 7 }).IsTrue();

		// Validity window (whole-second instants so the microsecond-precision TIMESTAMPTZ round-trips exactly).
		await Assert.That(got.Validity).IsNotNull();
		await Assert.That(got.Validity!.From).IsEqualTo(validFrom);
		await Assert.That(got.Validity.To).IsEqualTo(validTo);

		// Lifecycle timestamps: retained now, nothing else stamped yet.
		await Assert.That(got.RetainedAt).IsGreaterThanOrEqualTo(before.AddSeconds(-2));
		await Assert.That(got.LastAccessedAt).IsNull();
		await Assert.That(got.RetractedAt).IsNull();
		await Assert.That(got.SupersededAt).IsNull();
		await Assert.That(got.SupersededBy).IsNull();
	}

	// (b) --------------------------------------------------------------------------------------------

	[Test]
	public async ValueTask retain_returns_related_with_a_sane_similarity() {
		var embedder = await OnnxEmbeddingModel.RequireAsync();
		using var dir = new TempStore();
		using var store = new KontextStore(dir.Path, embedder, Dimensions);
		await store.EnsureCreatedAsync();

		var first = await store.RetainAsync(new Memory {
			Type = MemoryType.Fact,
			Content = "Sergio prefers hybrid search over pure vector search for memory recall",
		});
		var second = await store.RetainAsync(new Memory {
			Type = MemoryType.Fact,
			Content = "For recalling memories, hybrid search is preferred over vector-only search",
		});

		await Assert.That(second.Related.Count).IsGreaterThanOrEqualTo(1);
		var top = second.Related[0];
		await Assert.That(top.MemoryId).IsEqualTo(first.MemoryId);
		await Assert.That(top.Similarity).IsGreaterThanOrEqualTo(-1.0);
		await Assert.That(top.Similarity).IsLessThanOrEqualTo(1.0);
		await Assert.That(top.Similarity).IsGreaterThan(0.5);
	}

	// (c) --------------------------------------------------------------------------------------------

	[Test]
	public async ValueTask supersede_stamps_the_old_row_with_its_successor() {
		var embedder = await OnnxEmbeddingModel.RequireAsync();
		using var dir = new TempStore();
		using var store = new KontextStore(dir.Path, embedder, Dimensions);
		await store.EnsureCreatedAsync();

		var old = await store.RetainAsync(new Memory {
			Type = MemoryType.Observation,
			Content = "The projector checkpoint format switched to protobuf JSON in v25.1.",
		});
		var before = DateTimeOffset.UtcNow;
		var successor = await store.RetainAsync(new Memory {
			Type = MemoryType.Fact,
			Content = "The projector checkpoint format is protobuf binary since v25.2.",
			Supersedes = [old.MemoryId],
		});

		var stamped = await store.GetAsync(old.MemoryId);
		await Assert.That(stamped).IsNotNull();
		await Assert.That(stamped!.SupersededBy).IsEqualTo(successor.MemoryId);
		await Assert.That(stamped.SupersededAt).IsNotNull();
		await Assert.That(stamped.SupersededAt!.Value).IsGreaterThanOrEqualTo(before.AddSeconds(-2));

		// The successor itself is not superseded.
		var succ = await store.GetAsync(successor.MemoryId);
		await Assert.That(succ!.SupersededAt).IsNull();
		await Assert.That(succ.SupersededBy).IsNull();
	}

	// (d) --------------------------------------------------------------------------------------------

	[Test]
	public async ValueTask hybrid_recall_ranks_the_matching_memory_first_in_both_arms() {
		var embedder = await OnnxEmbeddingModel.RequireAsync();
		using var dir = new TempStore();
		using var store = new KontextStore(dir.Path, embedder, Dimensions);
		await store.EnsureCreatedAsync();

		var target = await store.RetainAsync(new Memory {
			Type = MemoryType.Fact,
			Content = "The projector checkpoint format switched to protobuf JSON in v25.1.",
		});
		await store.RetainAsync(new Memory { Type = MemoryType.Observation, Content = "The cluster gossip interval defaults to two seconds." });
		await store.RetainAsync(new Memory { Type = MemoryType.Procedure, Content = "Deploy process: run tests, pack nuget, push to the local feed." });
		await store.RetainAsync(new Memory { Type = MemoryType.Plan, Content = "Quarterly planning meeting notes about the budget forecast." });

		var lean = await store.RecallAsync("what happened to the projector checkpoint format?", new RecallOptions { Limit = 5 });
		await Assert.That(lean.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(lean[0]).IsTypeOf<RecalledMemory.Lean>();
		await Assert.That(((RecalledMemory.Lean)lean[0]).Memory.MemoryId).IsEqualTo(target.MemoryId);
		await Assert.That(lean[0].Score).IsGreaterThan(0);

		var full = await store.RecallAsync("what happened to the projector checkpoint format?", new RecallOptions { Limit = 5, IncludeFull = true });
		await Assert.That(full[0]).IsTypeOf<RecalledMemory.Full>();
		await Assert.That(((RecalledMemory.Full)full[0]).Memory.MemoryId).IsEqualTo(target.MemoryId);
		// The full arm carries the whole stored record.
		await Assert.That(((RecalledMemory.Full)full[0]).Memory.Content).IsEqualTo("The projector checkpoint format switched to protobuf JSON in v25.1.");
	}

	// (e) --------------------------------------------------------------------------------------------

	[Test]
	public async ValueTask recall_scoped_tag_filter_matches_scope_and_value_not_value_alone() {
		var embedder = await OnnxEmbeddingModel.RequireAsync();
		using var dir = new TempStore();
		using var store = new KontextStore(dir.Path, embedder, Dimensions);
		await store.EnsureCreatedAsync();

		var work = await store.RetainAsync(new Memory {
			Type = MemoryType.Fact,
			Content = "The checkpoint format is stored as protobuf.",
			Tags = [Work("alpha")],
		});
		var team = await store.RetainAsync(new Memory {
			Type = MemoryType.Fact,
			Content = "The checkpoint format is documented in the design note.",
			Tags = [Team("alpha")],
		});

		var byWork = await store.RecallAsync("checkpoint format", new RecallOptions { Limit = 10, Tags = [Work("alpha")] });
		await Assert.That(byWork.Count).IsEqualTo(1);
		await Assert.That(((RecalledMemory.Lean)byWork[0]).Memory.MemoryId).IsEqualTo(work.MemoryId);

		// The SAME value ("alpha") under a DIFFERENT scope must not match the work:alpha filter.
		await Assert.That(byWork.Any(h => ((RecalledMemory.Lean)h).Memory.MemoryId == team.MemoryId)).IsFalse();

		var byTeam = await store.RecallAsync("checkpoint format", new RecallOptions { Limit = 10, Tags = [Team("alpha")] });
		await Assert.That(byTeam.Count).IsEqualTo(1);
		await Assert.That(((RecalledMemory.Lean)byTeam[0]).Memory.MemoryId).IsEqualTo(team.MemoryId);
	}

	// (f) --------------------------------------------------------------------------------------------

	[Test]
	public async ValueTask recall_min_score_excludes_low_scoring_memories() {
		var embedder = await OnnxEmbeddingModel.RequireAsync();
		using var dir = new TempStore();
		using var store = new KontextStore(dir.Path, embedder, Dimensions);
		await store.EnsureCreatedAsync();

		await store.RetainAsync(new Memory { Type = MemoryType.Fact, Content = "The projector checkpoint format switched to protobuf JSON in v25.1." });
		await store.RetainAsync(new Memory { Type = MemoryType.Observation, Content = "The cluster gossip interval defaults to two seconds." });
		await store.RetainAsync(new Memory { Type = MemoryType.Plan, Content = "Quarterly planning meeting notes about the budget forecast." });

		var all = await store.RecallAsync("projector checkpoint format", new RecallOptions { Limit = 10 });
		await Assert.That(all.Count).IsGreaterThanOrEqualTo(2);

		var lowest = all.Min(h => h.Score);
		var highest = all.Max(h => h.Score);
		await Assert.That(highest).IsGreaterThan(lowest); // distinct scores over distinct texts
		var threshold = (lowest + highest) / 2.0;

		var filtered = await store.RecallAsync("projector checkpoint format", new RecallOptions { Limit = 10, MinScore = threshold });
		await Assert.That(filtered.All(h => h.Score >= threshold)).IsTrue();
		await Assert.That(filtered.Count).IsLessThan(all.Count);
	}

	// (g) --------------------------------------------------------------------------------------------

	[Test]
	public async ValueTask recollect_filters_by_type_and_sorts_by_importance_in_the_chosen_direction() {
		var embedder = await OnnxEmbeddingModel.RequireAsync();
		using var dir = new TempStore();
		using var store = new KontextStore(dir.Path, embedder, Dimensions);
		await store.EnsureCreatedAsync();

		await store.RetainAsync(new Memory { Type = MemoryType.Plan, Content = "Ship the connector spec review.", Importance = MemoryImportance.Critical });
		await store.RetainAsync(new Memory { Type = MemoryType.Plan, Content = "Tidy the playground project.", Importance = MemoryImportance.Low });
		await store.RetainAsync(new Memory { Type = MemoryType.Plan, Content = "Wire the reindex scheduler.", Importance = MemoryImportance.High });
		await store.RetainAsync(new Memory { Type = MemoryType.Fact, Content = "The cluster gossip interval defaults to two seconds.", Importance = MemoryImportance.Critical });

		var desc = await store.RecollectAsync(new RecollectOptions {
			Types = [MemoryType.Plan],
			Sort = RecollectSort.Importance,
			Direction = SortDirection.Descending,
		});
		await Assert.That(desc.Count).IsEqualTo(3); // the Fact is filtered out
		await Assert.That(desc.All(m => m.MemoryType == MemoryType.Plan)).IsTrue();
		await Assert.That(desc[0].Importance).IsEqualTo(MemoryImportance.Critical);
		await Assert.That(desc[1].Importance).IsEqualTo(MemoryImportance.High);
		await Assert.That(desc[2].Importance).IsEqualTo(MemoryImportance.Low);

		var asc = await store.RecollectAsync(new RecollectOptions {
			Types = [MemoryType.Plan],
			Sort = RecollectSort.Importance,
			Direction = SortDirection.Ascending,
		});
		await Assert.That(asc[0].Importance).IsEqualTo(MemoryImportance.Low);
		await Assert.That(asc[2].Importance).IsEqualTo(MemoryImportance.Critical);
	}

	// (h) --------------------------------------------------------------------------------------------

	[Test]
	public async ValueTask retract_hides_from_recall_and_recollect_but_keeps_the_row_and_is_idempotent() {
		var embedder = await OnnxEmbeddingModel.RequireAsync();
		using var dir = new TempStore();
		using var store = new KontextStore(dir.Path, embedder, Dimensions);
		await store.EnsureCreatedAsync();

		var retained = await store.RetainAsync(new Memory {
			Type = MemoryType.Observation,
			Content = "A memory about the projector checkpoint format that will be retracted.",
		});

		await store.RetractAsync(retained.MemoryId);

		var recall = await store.RecallAsync("projector checkpoint format", new RecallOptions { Limit = 10 });
		await Assert.That(recall.Any(h => ((RecalledMemory.Lean)h).Memory.MemoryId == retained.MemoryId)).IsFalse();

		var recollect = await store.RecollectAsync(new RecollectOptions());
		await Assert.That(recollect.Any(m => m.MemoryId == retained.MemoryId)).IsFalse();

		// The row survives as a soft delete, carrying RetractedAt.
		var got = await store.GetAsync(retained.MemoryId);
		await Assert.That(got).IsNotNull();
		await Assert.That(got!.RetractedAt).IsNotNull();

		// Retracting again is a no-op that still leaves the row retracted.
		await store.RetractAsync(retained.MemoryId);
		var again = await store.GetAsync(retained.MemoryId);
		await Assert.That(again!.RetractedAt).IsNotNull();
	}

	// (i) --------------------------------------------------------------------------------------------

	[Test]
	public async ValueTask mark_accessed_stamps_last_accessed_at() {
		var embedder = await OnnxEmbeddingModel.RequireAsync();
		using var dir = new TempStore();
		using var store = new KontextStore(dir.Path, embedder, Dimensions);
		await store.EnsureCreatedAsync();

		var retained = await store.RetainAsync(new Memory { Type = MemoryType.Observation, Content = "A memory whose recency clock is watched." });
		await Assert.That((await store.GetAsync(retained.MemoryId))!.LastAccessedAt).IsNull();

		var before = DateTimeOffset.UtcNow;
		await store.MarkAccessedAsync([retained.MemoryId]);

		var got = await store.GetAsync(retained.MemoryId);
		await Assert.That(got!.LastAccessedAt).IsNotNull();
		await Assert.That(got.LastAccessedAt!.Value).IsGreaterThanOrEqualTo(before.AddSeconds(-2));
	}

	// (j) --------------------------------------------------------------------------------------------

	[Test]
	public async ValueTask data_and_search_survive_dispose_and_reopen() {
		var embedder = await OnnxEmbeddingModel.RequireAsync();
		using var dir = new TempStore();

		string budgetId;
		using (var store = new KontextStore(dir.Path, embedder, Dimensions)) {
			await store.EnsureCreatedAsync();
			var budget = await store.RetainAsync(new Memory {
				Type = MemoryType.Plan,
				Content = "Quarterly planning meeting notes about the budget forecast.",
			});
			budgetId = budget.MemoryId;
			await store.RetainAsync(new Memory { Type = MemoryType.Observation, Content = "The cluster gossip interval defaults to two seconds." });
		}

		using (var reopened = new KontextStore(dir.Path, embedder, Dimensions)) {
			await reopened.EnsureCreatedAsync();

			var got = await reopened.GetAsync(budgetId);
			await Assert.That(got).IsNotNull();
			await Assert.That(got!.Content).IsEqualTo("Quarterly planning meeting notes about the budget forecast.");

			var hits = await reopened.RecallAsync("budget planning forecast", new RecallOptions { Limit = 3 });
			await Assert.That(hits.Count).IsGreaterThanOrEqualTo(1);
			await Assert.That(((RecalledMemory.Lean)hits[0]).Memory.MemoryId).IsEqualTo(budgetId);
		}
	}

	// (k) --------------------------------------------------------------------------------------------

	[Test]
	[Timeout(120_000)]
	public async ValueTask bulk_retain_past_the_index_floor_optimizes_and_still_recalls_correctly(CancellationToken cancellationToken) {
		var embedder = await OnnxEmbeddingModel.RequireAsync(cancellationToken);
		using var dir = new TempStore();
		using var store = new KontextStore(dir.Path, embedder, Dimensions);
		await store.EnsureCreatedAsync(cancellationToken);

		const string Marker = "the quick brown fox jumps over the lazy dog";
		var markerId = (await store.RetainAsync(new Memory { Type = MemoryType.Fact, Content = Marker }, ct: cancellationToken)).MemoryId;

		// Cross the 256-row IVF_PQ training floor so the lazy vector index is created (300 records total).
		for (var i = 0; i < 299; i++)
			await store.RetainAsync(new Memory { Type = MemoryType.Observation, Content = $"Routine memory number {i} about background topic {i % 17}." }, ct: cancellationToken);

		// Optimize folds the unindexed rows into the vector index and compacts the dataset.
		await store.OptimizeAsync(cancellationToken);

		var hits = await store.RecallAsync(Marker, new RecallOptions { Limit = 5 }, cancellationToken);
		await Assert.That(hits.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(((RecalledMemory.Lean)hits[0]).Memory.MemoryId).IsEqualTo(markerId);
	}

	// ------------------------------------------------------------------------------------------------
	// Test infrastructure.
	// ------------------------------------------------------------------------------------------------

	/// <summary>A unique temp directory owned by one <see cref="KontextStore"/>; deleted on dispose.</summary>
	sealed class TempStore : IDisposable {
		public string Path { get; }

		public TempStore() {
			this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontextstore-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(this.Path);
		}

		public void Dispose() {
			try {
				if (Directory.Exists(this.Path))
					Directory.Delete(this.Path, recursive: true);
			} catch (IOException) {
				// Best-effort cleanup; a lingering native handle must not fail the test.
			} catch (UnauthorizedAccessException) {
				// Best-effort cleanup.
			}
		}
	}

	/// <summary>
	/// Shared, process-wide REAL embedding generator backed by the SK ONNX Bert connector
	/// (<c>Microsoft.SemanticKernel.Connectors.Onnx</c>) over <c>TaylorAI/bge-micro-v2</c> (384-dim), cached
	/// under <c>~/.cache/ducklance-tests/bge-micro-v2/</c>. Downloaded once if absent; if the model is neither
	/// cached nor downloadable, <see cref="RequireAsync"/> raises <see cref="SkipTestException"/> so the test
	/// skips as an environment precondition rather than failing.
	/// </summary>
	static class OnnxEmbeddingModel {
		const string ModelUrl = "https://huggingface.co/TaylorAI/bge-micro-v2/resolve/main/onnx/model.onnx";
		const string VocabUrl = "https://huggingface.co/TaylorAI/bge-micro-v2/resolve/main/vocab.txt";

		static readonly SemaphoreSlim s_gate = new(1, 1);
		static IEmbeddingGenerator<string, Embedding<float>>? s_generator;
		static ServiceProvider? s_provider; // kept alive so the generator's native session stays valid
		static bool s_initialized;

		public static async Task<IEmbeddingGenerator<string, Embedding<float>>> RequireAsync(CancellationToken cancellationToken = default) {
			var generator = await GetAsync(cancellationToken);
			if (generator is null)
				throw new SkipTestException("embedding model unavailable (offline?) — expected at ~/.cache/ducklance-tests/bge-micro-v2/{model.onnx,vocab.txt}");
			return generator;
		}

		static async Task<IEmbeddingGenerator<string, Embedding<float>>?> GetAsync(CancellationToken cancellationToken) {
			if (s_initialized)
				return s_generator;

			await s_gate.WaitAsync(cancellationToken);
			try {
				if (!s_initialized) {
					s_generator = await CreateAsync(cancellationToken);
					s_initialized = true;
				}
				return s_generator;
			} finally {
				s_gate.Release();
			}
		}

		static async Task<IEmbeddingGenerator<string, Embedding<float>>?> CreateAsync(CancellationToken cancellationToken) {
			var cacheDir = System.IO.Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "ducklance-tests", "bge-micro-v2");
			Directory.CreateDirectory(cacheDir);

			var modelPath = System.IO.Path.Combine(cacheDir, "model.onnx");
			var vocabPath = System.IO.Path.Combine(cacheDir, "vocab.txt");

			var ready = File.Exists(modelPath) && File.Exists(vocabPath);
			if (!ready)
				ready = await TryDownloadAsync(modelPath, vocabPath, cancellationToken);
			if (!ready)
				return null;

			try {
				var services = new ServiceCollection();
				services.AddBertOnnxEmbeddingGenerator(modelPath, vocabPath);
				s_provider = services.BuildServiceProvider();
				return s_provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
			} catch (Exception ex) when (ex is IOException or InvalidOperationException) {
				// A cached-but-corrupt model/vocab pair loads as "unavailable" rather than failing every test.
				return null;
			}
		}

		static async Task<bool> TryDownloadAsync(string modelPath, string vocabPath, CancellationToken cancellationToken) {
			try {
				using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
				await DownloadToAsync(httpClient, ModelUrl, modelPath, cancellationToken);
				await DownloadToAsync(httpClient, VocabUrl, vocabPath, cancellationToken);
				return true;
			} catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException) {
				// Offline-tolerant: no network / timeout / read-only cache all just mean "unavailable here".
				TryDelete(modelPath);
				TryDelete(vocabPath);
				return false;
			}
		}

		static async Task DownloadToAsync(HttpClient httpClient, string url, string destinationPath, CancellationToken cancellationToken) {
			var tempPath = destinationPath + ".download";
			await using (var responseStream = await httpClient.GetStreamAsync(new Uri(url), cancellationToken))
			await using (var fileStream = File.Create(tempPath))
				await responseStream.CopyToAsync(fileStream, cancellationToken);
			File.Move(tempPath, destinationPath, overwrite: true);
		}

		static void TryDelete(string path) {
			try {
				if (File.Exists(path))
					File.Delete(path);
			} catch (IOException) {
				// Best-effort cleanup.
			} catch (UnauthorizedAccessException) {
				// Best-effort cleanup.
			}
		}
	}
}
