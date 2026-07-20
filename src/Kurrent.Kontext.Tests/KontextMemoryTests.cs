// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using Kurrent.Kontext.Data;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// Behavioral tests for the <see cref="KontextMemory"/> skeleton composed over
/// <see cref="KontextDataStore"/>, run against <see cref="TestVectorStore"/> (see its remarks for
/// why a test double stands in for a real connector). Each test gets an isolated store, so the
/// suite parallelizes freely.
/// </summary>
public class KontextMemoryTests {
	static KontextMemory NewMemory() => new(new KontextDataStore(new TestVectorStore(new TrigramHashEmbeddingGenerator())));

	static KontextDataStore NewBufferedStore(TestVectorStore store, int batchSize, TimeSpan batchWait) =>
		new(store, new() { Enabled = true, BatchSize = batchSize, BatchWait = batchWait });

	static TestMemoryCollection MemoriesOf(TestVectorStore store) =>
		(TestMemoryCollection)store.GetCollection<string, MemoryRecord>("memories");

	static Contracts.Memory Observation(string content) => new() {
		MemoryType = Contracts.MemoryType.Observation,
		Content    = content,
	};

	[Test]
	public async ValueTask retains_memories_and_assigns_server_ids() {
		// Arrange
		var memory = NewMemory();

		// Act
		var response = await memory.RetainAsync(new() {
			Memories = {
				Observation("The projector checkpoint format switched to protobuf JSON in v25.1."),
				Observation("The cluster gossip interval defaults to two seconds."),
			},
		});

		// Assert
		await Assert.That(response.Results.Count).IsEqualTo(2);
		await Assert.That(response.Results.All(r => Guid.TryParse(r.MemoryId, out _))).IsTrue();
	}

	[Test]
	public async ValueTask rejects_retaining_an_id_that_already_exists() {
		// Arrange
		var memory     = NewMemory();
		var suppliedId = Guid.CreateVersion7().ToString();
		await memory.RetainAsync(new() { Memories = { new Contracts.Memory { MemoryId = suppliedId, Content = "first write of this id" } } });

		// Act
		RpcException? exception = null;
		try {
			await memory.RetainAsync(new() { Memories = { new Contracts.Memory { MemoryId = suppliedId, Content = "same id again" } } });
		} catch (RpcException ex) {
			exception = ex;
		}

		// Assert
		await Assert.That(exception).IsNotNull();
		await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.AlreadyExists);
	}

	[Test]
	public async ValueTask recalls_memories_by_meaning_ranked_with_scores() {
		// Arrange
		var memory = NewMemory();
		var retain = await memory.RetainAsync(new() {
			Memories = {
				Observation("The projector checkpoint format switched to protobuf JSON in v25.1."),
				Observation("The cluster gossip interval defaults to two seconds."),
			},
		});
		var expectedId = retain.Results[0].MemoryId;

		// Act
		var recall = await memory.RecallAsync(new() { Query = "what happened to the projector checkpoint format?", Limit = 5 });

		// Assert
		await Assert.That(Guid.TryParse(recall.QueryId, out _)).IsTrue();
		await Assert.That(recall.Memories.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(recall.Memories[0].Lean.MemoryId).IsEqualTo(expectedId);
		await Assert.That(recall.Memories[0].Score).IsGreaterThan(0);
	}

	[Test]
	public async ValueTask recall_pre_filters_by_tags() {
		// Arrange
		var memory = NewMemory();
		var retain = await memory.RetainAsync(new() {
			Memories = {
				new Contracts.Memory {
					Content = "The projector checkpoint format switched to protobuf JSON in v25.1.",
					Tags    = { new Contracts.Tag { Scope = "project", Value = "kurrentdb" } },
				},
				Observation("The projector checkpoint format is discussed in the design doc."),
			},
		});
		var taggedId = retain.Results[0].MemoryId;

		// Act
		var matching = await memory.RecallAsync(new() {
			Query = "checkpoint format",
			Tags  = { new Contracts.Tag { Scope = "project", Value = "kurrentdb" } },
		});
		var nonMatching = await memory.RecallAsync(new() {
			Query = "checkpoint format",
			Tags  = { new Contracts.Tag { Scope = "project", Value = "somewhere-else" } },
		});

		// Assert
		await Assert.That(matching.Memories.Count).IsEqualTo(1);
		await Assert.That(matching.Memories[0].Lean.MemoryId).IsEqualTo(taggedId);
		await Assert.That(nonMatching.Memories.Count).IsEqualTo(0);
	}

	[Test]
	public async ValueTask recall_returns_the_folded_record_when_include_full_is_set() {
		// Arrange
		var memory = NewMemory();
		var retain = await memory.RetainAsync(new() { Memories = { Observation("The projector checkpoint format switched to protobuf JSON in v25.1.") } });
		var expectedId = retain.Results[0].MemoryId;

		// Act
		var recall = await memory.RecallAsync(new() { Query = "checkpoint format", Limit = 1, IncludeFull = true });

		// Assert
		await Assert.That(recall.Memories[0].Full).IsNotNull();
		await Assert.That(recall.Memories[0].Full.MemoryId).IsEqualTo(expectedId);
		await Assert.That(recall.Memories[0].Full.RetainedAt).IsNotNull();
	}

	[Test]
	public async ValueTask supersede_hides_the_old_memory_and_marks_it_with_its_successor() {
		// Arrange
		var memory = NewMemory();
		var retain = await memory.RetainAsync(new() { Memories = { Observation("The projector checkpoint format switched to protobuf JSON in v25.1.") } });
		var oldId  = retain.Results[0].MemoryId;

		// Act
		var supersede = await memory.RetainAsync(new() {
			Memories = {
				new Contracts.Memory {
					MemoryType = Contracts.MemoryType.Fact,
					Content    = "The projector checkpoint format is protobuf binary since v25.2.",
					Supersedes = { oldId },
				},
			},
		});
		var successorId = supersede.Results[0].MemoryId;

		// Assert
		var recall = await memory.RecallAsync(new() { Query = "projector checkpoint format", Limit = 10 });
		await Assert.That(recall.Memories.All(m => m.Lean.MemoryId != oldId)).IsTrue();
		await Assert.That(recall.Memories.Any(m => m.Lean.MemoryId == successorId)).IsTrue();

		var reclaimed = await memory.ReclaimAsync(new() { Ids = { oldId } }).ToListAsync();
		await Assert.That(reclaimed.Count).IsEqualTo(1);
		await Assert.That(reclaimed[0].SupersededAt).IsNotNull();
		await Assert.That(reclaimed[0].SupersededBy).IsEqualTo(successorId);
	}

	[Test]
	public async ValueTask reconcile_surfaces_look_alikes_without_blocking_the_write() {
		// Arrange
		var memory = NewMemory();
		var retain = await memory.RetainAsync(new() { Memories = { Observation("The projector checkpoint format is protobuf binary since v25.2.") } });
		var existingId = retain.Results[0].MemoryId;

		// Act
		var reconcile = await memory.RetainAsync(new() {
			Reconcile = true,
			Memories  = { Observation("The projector checkpoint format is protobuf binary since v25.2, per the release notes.") },
		});

		// Assert
		await Assert.That(reconcile.Results[0].Related.Any(r => r.MemoryId == existingId)).IsTrue();
		await Assert.That(Guid.TryParse(reconcile.Results[0].MemoryId, out _)).IsTrue();
	}

	[Test]
	public async ValueTask retract_cascades_to_memories_that_cite_the_retracted_one() {
		// Arrange
		var memory = NewMemory();
		var retain = await memory.RetainAsync(new() { Memories = { Observation("The projector checkpoint format is protobuf binary since v25.2.") } });
		var citedId = retain.Results[0].MemoryId;

		var derived = await memory.RetainAsync(new() {
			Memories = {
				new Contracts.Memory {
					MemoryType = Contracts.MemoryType.Summary,
					Content    = "Checkpoint formats evolved from JSON to binary across v25.",
					Evidence   = new Contracts.Evidence {
						Reasoning = "Consolidates the checkpoint format history.",
						Citations = { new Contracts.Evidence.Types.Citation { Memory = new Contracts.Evidence.Types.MemoryRef { Id = citedId, Position = 1 } } },
					},
				},
			},
		});
		var derivedId = derived.Results[0].MemoryId;

		// Act
		var retract = await memory.RetractAsync(new() { MemoryId = citedId, Reason = "never actually shipped" });

		// Assert
		await Assert.That(retract.RetractedMemoryIds).Contains(citedId);
		await Assert.That(retract.RetractedMemoryIds).Contains(derivedId);

		var recall = await memory.RecallAsync(new() { Query = "checkpoint format", Limit = 10 });
		await Assert.That(recall.Memories.All(m => m.Lean.MemoryId != citedId && m.Lean.MemoryId != derivedId)).IsTrue();
	}

	[Test]
	public async ValueTask retract_is_an_idempotent_no_op_for_absent_or_already_retracted_ids() {
		// Arrange
		var memory = NewMemory();
		var retain = await memory.RetainAsync(new() { Memories = { Observation("A memory that will be retracted.") } });
		var memoryId = retain.Results[0].MemoryId;
		await memory.RetractAsync(new() { MemoryId = memoryId });

		// Act
		var again  = await memory.RetractAsync(new() { MemoryId = memoryId });
		var absent = await memory.RetractAsync(new() { MemoryId = Guid.CreateVersion7().ToString() });

		// Assert
		await Assert.That(again.RetractedMemoryIds.Count).IsEqualTo(0);
		await Assert.That(absent.RetractedMemoryIds.Count).IsEqualTo(0);
	}

	[Test]
	public async ValueTask reclaim_returns_records_of_any_status_and_omits_unknown_ids() {
		// Arrange
		var memory = NewMemory();
		var retain = await memory.RetainAsync(new() { Memories = { Observation("A memory that will be retracted.") } });
		var retractedId = retain.Results[0].MemoryId;
		await memory.RetractAsync(new() { MemoryId = retractedId });

		// Act
		var reclaimed = await memory.ReclaimAsync(new() { Ids = { retractedId, Guid.CreateVersion7().ToString() } }).ToListAsync();

		// Assert
		await Assert.That(reclaimed.Count).IsEqualTo(1);
		await Assert.That(reclaimed[0].MemoryId).IsEqualTo(retractedId);
		await Assert.That(reclaimed[0].RetractedAt).IsNotNull();
	}

	[Test]
	public async ValueTask reclaim_refreshes_the_recency_clock() {
		// Arrange
		var memory = NewMemory();
		var retain = await memory.RetainAsync(new() { Memories = { Observation("A memory whose recency clock is watched.") } });
		var memoryId = retain.Results[0].MemoryId;

		// Act
		var first  = (await memory.ReclaimAsync(new() { Ids = { memoryId } }).ToListAsync())[0].LastAccessedAt.ToDateTimeOffset();
		var second = (await memory.ReclaimAsync(new() { Ids = { memoryId } }).ToListAsync())[0].LastAccessedAt.ToDateTimeOffset();

		// Assert
		await Assert.That(second).IsGreaterThan(first);
	}

	[Test]
	public async ValueTask recollect_sorts_by_importance_and_honors_the_limit() {
		// Arrange
		var memory = NewMemory();
		await memory.RetainAsync(new() {
			Memories = {
				new Contracts.Memory { MemoryType = Contracts.MemoryType.Plan, Content = "Ship the connector spec review.", Importance = Contracts.MemoryImportance.Critical },
				new Contracts.Memory { MemoryType = Contracts.MemoryType.Plan, Content = "Tidy the playground project.", Importance = Contracts.MemoryImportance.Low },
				new Contracts.Memory { MemoryType = Contracts.MemoryType.Plan, Content = "Wire the reindex scheduler.", Importance = Contracts.MemoryImportance.High },
			},
		});

		// Act
		var plans = await memory.RecollectAsync(new() {
			Types_ = { Contracts.MemoryType.Plan },
			Sort   = Contracts.RecollectSort.Importance,
			Limit  = 2,
		}).ToListAsync();

		// Assert
		await Assert.That(plans.Count).IsEqualTo(2);
		await Assert.That(plans[0].Importance).IsEqualTo(Contracts.MemoryImportance.Critical);
		await Assert.That(plans[1].Importance).IsEqualTo(Contracts.MemoryImportance.High);
	}

	[Test]
	public async ValueTask recollect_filters_by_any_of_types_and_never_returns_retracted() {
		// Arrange
		var memory = NewMemory();
		var retain = await memory.RetainAsync(new() {
			Memories = {
				new Contracts.Memory { MemoryType = Contracts.MemoryType.Fact, Content = "The cluster gossip interval defaults to two seconds." },
				new Contracts.Memory { MemoryType = Contracts.MemoryType.Plan, Content = "Ship the connector spec review." },
				new Contracts.Memory { MemoryType = Contracts.MemoryType.Fact, Content = "A fact that will be retracted." },
				Observation("An observation that must not appear in a fact/plan listing."),
			},
		});
		await memory.RetractAsync(new() { MemoryId = retain.Results[2].MemoryId });

		// Act
		var listed = await memory.RecollectAsync(new() { Types_ = { Contracts.MemoryType.Fact, Contracts.MemoryType.Plan } }).ToListAsync();

		// Assert
		await Assert.That(listed.All(m => m.MemoryType is Contracts.MemoryType.Fact or Contracts.MemoryType.Plan)).IsTrue();
		await Assert.That(listed.Any(m => m.MemoryType == Contracts.MemoryType.Fact)).IsTrue();
		await Assert.That(listed.Any(m => m.MemoryType == Contracts.MemoryType.Plan)).IsTrue();
		await Assert.That(listed.All(m => m.RetractedAt is null)).IsTrue();
	}

	[Test]
	public async ValueTask recollect_defaults_to_newest_first_and_keeps_superseded_history() {
		// Arrange
		var memory = NewMemory();
		var retain = await memory.RetainAsync(new() { Memories = { Observation("The original memory, soon superseded.") } });
		await memory.RetainAsync(new() {
			Memories = { new Contracts.Memory { Content = "The successor memory.", Supersedes = { retain.Results[0].MemoryId } } },
		});

		// Act
		var listed = await memory.RecollectAsync(new()).ToListAsync();

		// Assert
		await Assert.That(listed.Any(m => m.SupersededAt is not null)).IsTrue();
		await Assert.That(listed.Zip(listed.Skip(1)).All(p => p.First.RetainedAt.ToDateTimeOffset() >= p.Second.RetainedAt.ToDateTimeOffset())).IsTrue();
	}

	[Test]
	public async ValueTask retain_writes_the_whole_request_as_one_batch_upsert() {
		// Arrange
		var store  = new TestVectorStore(new TrigramHashEmbeddingGenerator());
		var memory = new KontextMemory(new KontextDataStore(store));

		// Act
		await memory.RetainAsync(new() {
			Memories = {
				Observation("The projector checkpoint format switched to protobuf JSON in v25.1."),
				Observation("The cluster gossip interval defaults to two seconds."),
				Observation("The reindex scheduler runs on a single timer per dataset."),
			},
		});

		// Assert — one connector call (one commit, one embedding batch) covering all three records.
		var memories = MemoriesOf(store);
		await Assert.That(memories.UpsertBatchCalls).IsEqualTo(1);
		await Assert.That(memories.UpsertedRecords).IsEqualTo(3);
	}

	[Test]
	public async ValueTask supersede_resolves_ids_retained_earlier_in_the_same_batch() {
		// Arrange — the proto explicitly allows referencing a memory within the same batch via a
		// client-supplied id.
		var memory     = NewMemory();
		var originalId = Guid.CreateVersion7().ToString();

		// Act
		var retain = await memory.RetainAsync(new() {
			Memories = {
				new Contracts.Memory { MemoryId = originalId, Content = "The original memory, superseded within its own batch." },
				new Contracts.Memory { Content = "The successor memory.", Supersedes = { originalId } },
			},
		});
		var successorId = retain.Results[1].MemoryId;

		// Assert
		var reclaimed = await memory.ReclaimAsync(new() { Ids = { originalId } }).ToListAsync();
		await Assert.That(reclaimed.Count).IsEqualTo(1);
		await Assert.That(reclaimed[0].SupersededAt).IsNotNull();
		await Assert.That(reclaimed[0].SupersededBy).IsEqualTo(successorId);
	}

	[Test]
	public async ValueTask rejects_a_supplied_id_duplicated_within_one_batch() {
		// Arrange
		var memory     = NewMemory();
		var suppliedId = Guid.CreateVersion7().ToString();

		// Act
		RpcException? exception = null;
		try {
			await memory.RetainAsync(new() {
				Memories = {
					new Contracts.Memory { MemoryId = suppliedId, Content = "first use of the id" },
					new Contracts.Memory { MemoryId = suppliedId, Content = "second use of the id" },
				},
			});
		} catch (RpcException ex) {
			exception = ex;
		}

		// Assert
		await Assert.That(exception).IsNotNull();
		await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.AlreadyExists);
	}

	[Test]
	public async ValueTask buffered_touches_coalesce_per_memory_and_flush_on_dispose() {
		// Arrange
		var store     = new TestVectorStore(new TrigramHashEmbeddingGenerator());
		var dataStore = NewBufferedStore(store, batchSize: 100, batchWait: TimeSpan.FromMinutes(10));
		var memory    = new KontextMemory(dataStore);
		await memory.RetainAsync(new() { Memories = { Observation("A memory recalled repeatedly.") } });

		// Recollect never touches, so it reads the clocks without advancing them.
		var retainedAt = (await memory.RecollectAsync(new()).ToListAsync())[0].RetainedAt.ToDateTimeOffset();

		var memories      = MemoriesOf(store);
		var batchesBefore = memories.UpsertBatchCalls;
		var recordsBefore = memories.UpsertedRecords;

		// Act — three recalls buffer three touches of the same memory; nothing hits the store until
		// disposal flushes the coalesced touch.
		await Task.Delay(10); // ensure the touch timestamp is strictly after retained_at
		for (var i = 0; i < 3; i++)
			await memory.RecallAsync(new() { Query = "memory recalled repeatedly", Limit = 1 });

		var flushesBeforeDispose = memories.UpsertBatchCalls - batchesBefore;
		await dataStore.DisposeAsync();

		// Assert — one flush of one coalesced record, and the recency clock advanced.
		await Assert.That(flushesBeforeDispose).IsEqualTo(0);
		await Assert.That(memories.UpsertBatchCalls - batchesBefore).IsEqualTo(1);
		await Assert.That(memories.UpsertedRecords - recordsBefore).IsEqualTo(1);

		var listed = await memory.RecollectAsync(new()).ToListAsync();
		await Assert.That(listed[0].LastAccessedAt.ToDateTimeOffset()).IsGreaterThan(retainedAt);
	}

	[Test]
	public async ValueTask buffered_touches_flush_when_the_batch_size_is_reached() {
		// Arrange
		var store  = new TestVectorStore(new TrigramHashEmbeddingGenerator());
		var memory = new KontextMemory(NewBufferedStore(store, batchSize: 2, batchWait: TimeSpan.FromMinutes(10)));
		await memory.RetainAsync(new() {
			Memories = {
				Observation("The projector checkpoint format switched to protobuf JSON in v25.1."),
				Observation("The projector checkpoint format is protobuf binary since v25.2."),
			},
		});

		var memories      = MemoriesOf(store);
		var recordsBefore = memories.UpsertedRecords;

		// Act — one recall returns both memories: two distinct pending touches reach BatchSize.
		await memory.RecallAsync(new() { Query = "projector checkpoint format", Limit = 10 });

		// Assert — the size-triggered flush is fire-and-forget, so poll briefly for it to land.
		var flushed = false;
		for (var i = 0; i < 100 && !flushed; i++) {
			flushed = memories.UpsertedRecords - recordsBefore == 2;
			if (!flushed)
				await Task.Delay(50);
		}
		await Assert.That(flushed).IsTrue();
	}

	[Test]
	public async ValueTask buffered_touches_flush_after_the_batch_wait() {
		// Arrange
		var store  = new TestVectorStore(new TrigramHashEmbeddingGenerator());
		var memory = new KontextMemory(NewBufferedStore(store, batchSize: 100, batchWait: TimeSpan.FromMilliseconds(100)));
		await memory.RetainAsync(new() { Memories = { Observation("A memory touched once and left to linger.") } });

		var memories      = MemoriesOf(store);
		var recordsBefore = memories.UpsertedRecords;

		// Act — a single touch: far below BatchSize, so only the linger timer can flush it.
		await memory.RecallAsync(new() { Query = "touched once and left to linger", Limit = 1 });

		// Assert
		var flushed = false;
		for (var i = 0; i < 100 && !flushed; i++) {
			flushed = memories.UpsertedRecords - recordsBefore == 1;
			if (!flushed)
				await Task.Delay(50);
		}
		await Assert.That(flushed).IsTrue();
	}

	[Test]
	public async ValueTask reflect_is_not_implemented_in_the_skeleton() {
		// Arrange
		var memory = NewMemory();

		// Act
		NotImplementedException? exception = null;
		try {
			await memory.ReflectAsync(new() { Query = "what have I learned?" });
		} catch (NotImplementedException ex) {
			exception = ex;
		}

		// Assert
		await Assert.That(exception).IsNotNull();
	}
}
