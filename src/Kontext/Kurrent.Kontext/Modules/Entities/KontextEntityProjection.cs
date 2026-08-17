// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using System.Text;
using Kurrent.Kontext.Contracts;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Modules.Entities.Extraction;
using Kurrent.Kontext.Modules.Entities.Resolution;
using Kurrent.Surge;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Modules.Entities;

/// <summary>One entity's target state for the batch — created this batch or folded onto a stored row.</summary>
public sealed class EntityWrite {
	public required string  EntityId       { get; init; }
	public required string  Name           { get; init; }
	public required string  NormalizedName { get; init; }
	public required string  EntityType     { get; init; }
	public required string  Subtype        { get; init; }
	public required bool    IsNew          { get; init; }
	public required float[] Embedding      { get; init; }

	public List<string> Aliases     { get; init; } = [];
	public double       Confidence  { get; set; }
	public long         FirstSeen   { get; set; }
	public long         LastSeen    { get; set; }
	public long         LogPosition { get; set; }

	public void AddAlias(string normalized) {
		if (!Aliases.Contains(normalized))
			Aliases.Add(normalized);
	}

	internal EntityRow AsRow() => new() {
		EntityId       = EntityId,
		Name           = Name,
		NormalizedName = NormalizedName,
		EntityType     = EntityType,
		Subtype        = Subtype,
		Aliases        = Aliases,
		Confidence     = Confidence,
		FirstSeen      = FirstSeen,
		LastSeen       = LastSeen,
	};
}

/// <summary>One provenance row to append: where an entity surfaced, verbatim.</summary>
public sealed record MentionWrite(
	string EntityId, string MemoryId, string Surface, int? StartPos, int? EndPos,
	double Confidence, string Extractor, long RetainedAt, long LogPosition);

/// <summary>One suspected-duplicate pair to record for review.</summary>
public sealed record LinkWrite(string SourceEntityId, string TargetEntityId, double Confidence, string Method, long CreatedAt, long LogPosition);

/// <summary>
/// Everything one projected batch wants persisted, computed and final: the writer executes this
/// verbatim — it decides nothing.
/// </summary>
public sealed record EntityDelta {
	public static readonly EntityDelta Empty = new();

	public IReadOnlyCollection<EntityWrite> Entities { get; init; } = [];
	public IReadOnlyList<MentionWrite>      Mentions { get; init; } = [];
	public IReadOnlyList<LinkWrite>         Links    { get; init; } = [];

	public bool IsEmpty => Entities.Count == 0 && Mentions.Count == 0 && Links.Count == 0;
}

/// <summary>
/// The entity read model's state computation — the Kontext counterpart of the connectors-plane
/// state projections: events in, target state out, no persistence anywhere in it. One consumed
/// batch of memory events folds into one <see cref="EntityDelta"/> through extraction →
/// grouping → one batched embedding call → sequential resolution → threshold grading.
///
/// Only <see cref="MemoriesRetained"/> is consumed. Retraction does not unwind mentions on
/// purpose: a mention is history — the memory DID say it — and recall-side filtering is the
/// read model's job, not the ledger's.
/// </summary>
public sealed class KontextEntityProjection {
	readonly EntityExtractionPipeline _pipeline;

	readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
	readonly EmbeddingGenerationOptions                    _embeddingOptions;
	readonly EntityDeduplicator                            _deduplicator;

	/// <param name="store">
	/// Resolution's candidate source. MUST read through the projector's write connection: an
	/// attached lance catalog serves each connection the dataset view it first scanned, so only
	/// the write connection is guaranteed to see the batches already applied.
	/// </param>
	public KontextEntityProjection(
		EntityExtractionPipeline pipeline,
		IEmbeddingGenerator<string, Embedding<float>> embeddings,
		EmbeddingGenerationOptions embeddingOptions,
		KontextEntityStore store,
		EntityDeduplicationOptions? dedupOptions = null
	) {
		_pipeline         = pipeline;
		_embeddings       = embeddings;
		_embeddingOptions = embeddingOptions;
		_deduplicator     = new(CompositeEntityResolver.Over(store), dedupOptions);
	}

	// One occurrence group: every mention of the same (normalized name, type) across the batch.
	sealed class OccurrenceGroup(ExtractedEntity first) {
		public string Name           { get; } = first.Name;
		public string NormalizedName { get; } = first.NormalizedName;
		public string EntityType     { get; } = first.Type;
		public string Subtype        { get; private set; } = first.Subtype ?? "";
		public double Confidence     { get; private set; }
		public long   FirstSeen      { get; private set; } = long.MaxValue;
		public long   LastSeen       { get; private set; }
		public long   LogPosition    { get; private set; }

		public List<(ExtractedEntity Entity, string MemoryId, long RetainedAt, long Position)> Mentions { get; } = [];

		public void Add(ExtractedEntity entity, string memoryId, long retainedAt, long position) {
			Mentions.Add((entity, memoryId, retainedAt, position));
			Confidence  = Math.Max(Confidence, entity.Confidence);
			FirstSeen   = Math.Min(FirstSeen, retainedAt);
			LastSeen    = Math.Max(LastSeen, retainedAt);
			LogPosition = Math.Max(LogPosition, position);

			if (Subtype.Length == 0 && entity.Subtype is { Length: > 0 } subtype)
				Subtype = subtype;
		}
	}

	/// <summary>
	/// Folds one consumed batch into its delta: extract every retained memory, group the
	/// occurrences per (normalized name, type), embed the group names in ONE model call, then
	/// decide each group sequentially — in-batch state first, store second.
	/// </summary>
	public async ValueTask<EntityDelta> ProjectAsync(IReadOnlyList<SurgeRecord> batch, CancellationToken ct = default) {
		var groups = await ExtractGroupsAsync(batch, ct).ConfigureAwait(false);

		if (groups.Count == 0)
			return EntityDelta.Empty;

		// One model call for the whole batch — per-group embedding would be the dominant cost.
		// The group's first-seen SURFACE form embeds, matching how stored entities were embedded.
		var generated = await _embeddings
			.GenerateAsync(groups.Select(group => group.Name), _embeddingOptions, ct)
			.ConfigureAwait(false);

		var groupEmbeddings = groups.Zip(generated, (group, embedding) => (group, embedding.Vector.ToArray())).ToList();

		// Sequential on purpose: a group's decision must see the entities earlier groups created
		// or touched in this same batch — without it "Jon Smith" and "John Smith" arriving
		// together both miss and land as two UNRELATED entities, rather than merging (≥ auto-merge)
		// or at least coming out linked (≥ flag).
		var pending  = new Dictionary<string, EntityWrite>();
		var mentions = new List<MentionWrite>();
		var links    = new List<LinkWrite>();

		foreach (var (group, embedding) in groupEmbeddings) {
			ct.ThrowIfCancellationRequested();

			var probe = new ResolutionProbe(
				new EntityProbe(group.Name, group.EntityType, embedding),
				[.. pending.Values.Select(entity => new PendingEntity(entity.AsRow(), entity.Embedding))]);

			var decision = await _deduplicator.DecideAsync(probe, ct).ConfigureAwait(false);

			var target = decision.Action switch {
				DeduplicationAction.Merge => MergeInto(decision.Match!, group, pending),
				DeduplicationAction.Flag  => CreateNew(group, embedding, pending, FlagAgainst(decision, links, group)),
				_                         => CreateNew(group, embedding, pending),
			};

			foreach (var (entity, memoryId, retainedAt, position) in group.Mentions)
				mentions.Add(new(
					target.EntityId, memoryId, entity.Name, entity.Start, entity.End,
					entity.Confidence, entity.Extractor ?? "", retainedAt, position));
		}

		return new() { Entities = pending.Values, Mentions = mentions, Links = links };
	}

	async ValueTask<List<OccurrenceGroup>> ExtractGroupsAsync(IReadOnlyList<SurgeRecord> batch, CancellationToken ct) {
		var groups = new Dictionary<string, OccurrenceGroup>();
		var order  = new List<OccurrenceGroup>();

		foreach (var record in batch) {
			if (record.Value is not MemoriesRetained retained) continue;

			var position   = (long)record.LogPosition.CommitPosition!;
			var retainedAt = KontextDataStore.EncodeTimestamp(retained.RetainedAt);

			foreach (var entry in retained.Memories) {
				var extraction = await _pipeline.ExtractAsync(entry.Memory.Content, ct).ConfigureAwait(false);

				foreach (var entity in extraction.Entities) {
					if (!groups.TryGetValue(entity.Key, out var group)) {
						groups[entity.Key] = group = new(entity);
						order.Add(group);
					}

					group.Add(entity, entry.MemoryId, retainedAt, position);
				}
			}
		}

		return order;
	}

	static EntityWrite MergeInto(EntityRow match, OccurrenceGroup group, Dictionary<string, EntityWrite> pending) {
		if (!pending.TryGetValue(match.EntityId, out var entity)) {
			// First touch of a STORED entity this batch: start its pending state from the row.
			pending[match.EntityId] = entity = new() {
				EntityId       = match.EntityId,
				Name           = match.Name,
				NormalizedName = match.NormalizedName,
				EntityType     = match.EntityType,
				Subtype        = match.Subtype,
				IsNew          = false,
				Embedding      = [],
				Aliases        = [.. match.Aliases],
				Confidence     = match.Confidence,
				FirstSeen      = match.FirstSeen,
				LastSeen       = match.LastSeen,
			};
		}

		entity.AddAlias(group.NormalizedName);
		entity.Confidence  = Math.Max(entity.Confidence, group.Confidence);
		entity.LastSeen    = Math.Max(entity.LastSeen, group.LastSeen);
		entity.LogPosition = Math.Max(entity.LogPosition, group.LogPosition);

		return entity;
	}

	static EntityWrite CreateNew(OccurrenceGroup group, float[] embedding, Dictionary<string, EntityWrite> pending, string? entityId = null) {
		var entity = new EntityWrite {
			EntityId       = entityId ?? MintEntityId(group.EntityType, group.NormalizedName),
			Name           = group.Name,
			NormalizedName = group.NormalizedName,
			EntityType     = group.EntityType,
			Subtype        = group.Subtype,
			IsNew          = true,
			Embedding      = embedding,
			Aliases        = [group.NormalizedName],
			Confidence     = group.Confidence,
			FirstSeen      = group.FirstSeen,
			LastSeen       = group.LastSeen,
			LogPosition    = group.LogPosition,
		};

		pending[entity.EntityId] = entity;
		return entity;
	}

	// A flag creates the new entity AND records the pending link to the suspect — the id must
	// exist before the link row can reference it, hence the mint-first shape.
	static string FlagAgainst(DeduplicationDecision decision, List<LinkWrite> links, OccurrenceGroup group) {
		var entityId = MintEntityId(group.EntityType, group.NormalizedName);

		links.Add(new(
			entityId, decision.Match!.EntityId, decision.Score,
			decision.Method.ToString().ToLowerInvariant(), group.LastSeen, group.LogPosition));

		return entityId;
	}

	/// <summary>
	/// Deterministic entity identity: a replayed batch must upsert the SAME row it created the
	/// first time, so the id derives from what made the entity new — its type and the normalized
	/// name it was created under. Later aliases do not move it.
	/// </summary>
	public static string MintEntityId(string entityType, string normalizedName) {
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{entityType}\n{normalizedName}"));
		return $"ent-{Convert.ToHexStringLower(hash.AsSpan(0, 16))}";
	}
}
