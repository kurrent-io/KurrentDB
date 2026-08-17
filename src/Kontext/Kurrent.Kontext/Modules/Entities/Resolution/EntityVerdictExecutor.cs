// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Quack;

namespace Kurrent.Kontext.Modules.Entities.Resolution;

/// <summary>The states an entity_links row moves through: 'pending' at birth, either terminal after review.</summary>
public static class EntityLinkStatus {
	public const string Pending   = "pending";
	public const string Confirmed = "confirmed";
	public const string Rejected  = "rejected";
}

/// <summary>A review's ruling on one filed doubt.</summary>
public enum EntityLinkVerdict {
	/// <summary>One real-world thing under two entries: merge, one entry survives.</summary>
	SameEntity,

	/// <summary>Two things that merely looked alike: both entries stand, the doubt is settled for good.</summary>
	DifferentEntities,
}

/// <summary>What applying one verdict actually did — the reviewer's receipt.</summary>
public sealed record EntityLinkResolution {
	public required string SourceEntityId { get; init; }
	public required string TargetEntityId { get; init; }

	/// <summary>The status the link now carries.</summary>
	public required string Status { get; init; }

	/// <summary>The entry that survived the merge; empty on a rejection.</summary>
	public string SurvivorEntityId { get; init; } = "";

	/// <summary>The entry folded into the survivor and deleted; empty on a rejection.</summary>
	public string MergedEntityId { get; init; } = "";

	/// <summary>The survivor's mention count after the merge, recounted from the mentions table.</summary>
	public long SurvivorMentionCount { get; init; }

	/// <summary>Mention rows refiled from the merged entry onto the survivor.</summary>
	public int MentionsRefiled { get; init; }

	/// <summary>Other pending doubts that referenced the merged entry and now reference the survivor.</summary>
	public int LinksRepointed { get; init; }

	/// <summary>Other pending doubts retired instead of repointed, because they would have become self-links or duplicates.</summary>
	public int LinksDropped { get; init; }

	/// <summary>True when the link was already decided, so this call changed nothing.</summary>
	public bool WasAlreadyDecided { get; init; }
}

/// <summary>
/// Applies one review verdict to one entity_links row: the merge — the pipeline's single
/// irreversible move — or the rejection that records a decision so the doubt is never
/// re-litigated. Runs on the caller's connection and decides nothing about WHEN it runs;
/// <see cref="EntityWriteGate"/> owns that. Not thread safe: one turn drives it.
///
/// Every write with an idempotent MERGE shape goes through <see cref="KontextEntityWriter"/>
/// rather than being restated here — that is where the alias fold, the recount-never-increment
/// rule and the insert-if-absent link arm already live. The executor adds only the statements the
/// writer has no arm for: the mention refile, the two deletes, and the status flip.
///
/// Crash safety (lance commits per statement, so a crash lands BETWEEN statements — the same
/// window <see cref="KontextEntityWriter"/>'s replay reasoning covers):
/// - mentions refile onto the survivor BEFORE the loser's row is deleted, so no mention is ever
///   stranded on an entity that no longer exists
/// - other pending links are repointed, then their originals dropped, BEFORE the loser's row is
///   deleted, so no PENDING doubt is ever left pointing at a deleted entity
/// - the resolved link flips to its terminal status LAST, so every earlier crash point leaves it
///   'pending': the review queue still holds the to-do, and re-applying the verdict finishes the
///   merge from wherever it stopped
/// - every statement is a MERGE on a natural key or a DELETE by id, so each is a no-op the second
///   time — re-applying a verdict that already landed changes nothing
///
/// Durable against replay, both halves: the writer's link MERGE inserts only WHEN NOT MATCHED, so
/// the decided row keeps its verdict when the batch that filed the doubt replays, and by then the
/// loser's name is one of the survivor's aliases — so the replayed occurrence resolves INTO the
/// survivor rather than re-creating what the merge removed.
///
/// The survivor's IDENTITY columns are never touched, the writer's rule unchanged: a merge folds
/// spellings, mentions and recency onto the survivor, it does not rename it and does not move its
/// first_seen.
/// </summary>
public sealed class EntityVerdictExecutor(DuckDBAdvancedConnection connection, int dimension) {
	readonly KontextEntityStore  _store  = new(connection);
	readonly KontextEntityWriter _writer = new(connection, dimension);

	/// <summary>
	/// Applies <paramref name="verdict"/> to the link between the two entities.
	/// <paramref name="survivorEntityId"/> overrides survivor selection with the reviewer's choice
	/// (the "Emilia is a typo" case, where the human knows which spelling is the real one) and must
	/// name one of the two endpoints; omitted, the default rule picks.
	/// </summary>
	public async ValueTask<EntityLinkResolution> ApplyAsync(
		string sourceEntityId,
		string targetEntityId,
		EntityLinkVerdict verdict,
		string? survivorEntityId = null,
		CancellationToken ct = default
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceEntityId);
		ArgumentException.ThrowIfNullOrWhiteSpace(targetEntityId);

		// A doubt joins two entries by construction. Refusing the degenerate pair here is what
		// keeps a malformed request from folding an entity onto itself and then deleting it.
		if (sourceEntityId == targetEntityId)
			throw new ArgumentException($"The link endpoints are the same entity '{sourceEntityId}': a doubt joins two entries.", nameof(targetEntityId));

		if (survivorEntityId is { Length: > 0 } && survivorEntityId != sourceEntityId && survivorEntityId != targetEntityId) {
			throw new ArgumentException(
				$"The survivor '{survivorEntityId}' is not an endpoint of the link '{sourceEntityId}' → '{targetEntityId}'.",
				nameof(survivorEntityId));
		}

		var link = ReadLink(sourceEntityId, targetEntityId)
		        ?? throw new InvalidOperationException($"No entity link exists for '{sourceEntityId}' → '{targetEntityId}'.");

		// A decided doubt is never re-litigated — including by a second call carrying the same
		// verdict, which is what makes re-application a no-op rather than a second merge.
		if (link.Status != EntityLinkStatus.Pending)
			return new() { SourceEntityId = sourceEntityId, TargetEntityId = targetEntityId, Status = link.Status, WasAlreadyDecided = true };

		if (verdict is EntityLinkVerdict.DifferentEntities) {
			SetStatus(sourceEntityId, targetEntityId, EntityLinkStatus.Rejected);

			return new() { SourceEntityId = sourceEntityId, TargetEntityId = targetEntityId, Status = EntityLinkStatus.Rejected };
		}

		var source = await _store.GetAsync(sourceEntityId, ct).ConfigureAwait(false);
		var target = await _store.GetAsync(targetEntityId, ct).ConfigureAwait(false);

		// Neither endpoint is left (both swept by cleanup, say): there is nothing to fold, and the
		// verdict is still worth recording so the row cannot be reborn 'pending' by a replay.
		if (source is null && target is null) {
			SetStatus(sourceEntityId, targetEntityId, EntityLinkStatus.Confirmed);

			return new() { SourceEntityId = sourceEntityId, TargetEntityId = targetEntityId, Status = EntityLinkStatus.Confirmed };
		}

		var survivor = ChooseSurvivor(source, target, survivorEntityId);
		var loserId  = survivor.EntityId == sourceEntityId ? targetEntityId : sourceEntityId;
		var loser    = loserId == sourceEntityId ? source : target;

		var refiled = RefileMentions(loserId, survivor.EntityId);
		var repoint = PlanRepoint(loserId, survivor.EntityId, sourceEntityId, targetEntityId);

		// One writer pass: recount the survivor off the refiled mentions, fold the loser's
		// spellings and recency into it, and file the repointed doubts against it.
		_writer.Apply(new() { Entities = [SurvivorWrite(survivor, loser)], Links = repoint.Links });

		foreach (var (retiredSource, retiredTarget) in repoint.Retired)
			DeleteLink(retiredSource, retiredTarget);

		DeleteEntity(loserId);

		SetStatus(sourceEntityId, targetEntityId, EntityLinkStatus.Confirmed);

		var merged = await _store.GetAsync(survivor.EntityId, ct).ConfigureAwait(false);

		return new() {
			SourceEntityId       = sourceEntityId,
			TargetEntityId       = targetEntityId,
			Status               = EntityLinkStatus.Confirmed,
			SurvivorEntityId     = survivor.EntityId,
			MergedEntityId       = loserId,
			SurvivorMentionCount = merged?.MentionCount ?? 0,
			MentionsRefiled      = refiled,
			LinksRepointed       = repoint.Links.Count,
			LinksDropped         = repoint.Dropped,
		};
	}

	/// <summary>
	/// The id the default rule would keep for this pair, empty when neither entry is left — the
	/// review surface's preview of <see cref="ChooseSurvivor"/>, so a reviewer sees the same choice
	/// the executor would make.
	/// </summary>
	public static string PreviewSurvivor(EntityRow? source, EntityRow? target) =>
		source is null && target is null ? "" : ChooseSurvivor(source, target, null).EntityId;

	/// <summary>
	/// The default survivor: the entry carrying more mentions — it is the one more memories, more
	/// aliases and more of the read path already point at — with the earlier first_seen breaking a
	/// tie, and the id breaking that, so the same pair always resolves the same way whichever
	/// direction the link was filed in. A reviewer's explicit choice outranks all of it.
	/// </summary>
	static EntityRow ChooseSurvivor(EntityRow? source, EntityRow? target, string? survivorEntityId) {
		if (survivorEntityId is { Length: > 0 }) {
			var chosen = source?.EntityId == survivorEntityId ? source : target?.EntityId == survivorEntityId ? target : null;

			return chosen ?? throw new InvalidOperationException(
				$"The chosen survivor '{survivorEntityId}' no longer exists, so the merge cannot fold onto it.");
		}

		if (source is null)
			return target!;

		if (target is null)
			return source;

		if (source.MentionCount != target.MentionCount)
			return source.MentionCount > target.MentionCount ? source : target;

		if (source.FirstSeen != target.FirstSeen)
			return source.FirstSeen < target.FirstSeen ? source : target;

		return string.CompareOrdinal(source.EntityId, target.EntityId) <= 0 ? source : target;
	}

	/// <summary>
	/// The survivor's target state: its own row plus the loser's spellings. Aliases are ABSOLUTE
	/// for the writer, so the union is computed here; mention_count is left to the writer's recount
	/// and never passed in.
	/// </summary>
	static EntityWrite SurvivorWrite(EntityRow survivor, EntityRow? loser) {
		var write = new EntityWrite {
			EntityId       = survivor.EntityId,
			Name           = survivor.Name,
			NormalizedName = survivor.NormalizedName,
			EntityType     = survivor.EntityType,
			Subtype        = survivor.Subtype,
			IsNew          = false,
			Embedding      = [],
			Aliases        = [.. survivor.Aliases],
			Confidence     = survivor.Confidence,
			FirstSeen      = survivor.FirstSeen,
			LastSeen       = survivor.LastSeen,
			LogPosition    = survivor.LogPosition,
		};

		write.AddAlias(survivor.NormalizedName);

		if (loser is null)
			return write;

		// The loser's own spelling and every spelling it had collected become the survivor's: that
		// is what stops the next mention of the loser's name from splitting back off into a fresh
		// entity, and it is the half of the merge the read path sees.
		write.AddAlias(loser.NormalizedName);

		foreach (var alias in loser.Aliases)
			write.AddAlias(alias);

		write.Confidence  = Math.Max(write.Confidence, loser.Confidence);
		write.LastSeen    = Math.Max(write.LastSeen, loser.LastSeen);
		write.LogPosition = Math.Max(write.LogPosition, loser.LogPosition);

		return write;
	}

	// One statement, so no window exists where a mention sits under both entities or neither. The
	// join key IS the updated column, which the lance MERGE supports (pinned by
	// EntityResolutionLanceProbeTests).
	int RefileMentions(string loserId, string survivorId) {
		const string sql =
			"""
			MERGE INTO ldb.main.entity_mentions AS t
			USING (SELECT $loser_id AS entity_id) AS s
			ON t.entity_id = s.entity_id
			WHEN MATCHED THEN UPDATE SET entity_id = $survivor_id
			""";

		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.Add(new DuckDBParameter("loser_id", loserId));
		command.Parameters.Add(new DuckDBParameter("survivor_id", survivorId));

		return command.ExecuteNonQuery();
	}

	/// <summary>
	/// Every OTHER pending doubt touching the loser, planned as the survivor-facing rows to file
	/// and the originals to retire. A repointed pair that would name the same entity twice, or one
	/// the ledger already carries under any status, is retired without a replacement — the doubt
	/// either answered itself or has already been ruled on.
	/// </summary>
	Repoint PlanRepoint(string loserId, string survivorId, string decidedSource, string decidedTarget) {
		var links   = new List<LinkWrite>();
		var retired = new List<(string Source, string Target)>();
		var planned = new HashSet<(string Source, string Target)>();
		var dropped = 0;

		foreach (var row in ReadPendingLinksTouching(loserId)) {
			if (row.Source == decidedSource && row.Target == decidedTarget)
				continue;

			var source = row.Source == loserId ? survivorId : row.Source;
			var target = row.Target == loserId ? survivorId : row.Target;

			retired.Add((row.Source, row.Target));

			if (source == target || !planned.Add((source, target)) || LinkExists(source, target)) {
				dropped++;
				continue;
			}

			links.Add(new(source, target, row.Confidence, row.Method, row.CreatedAt, row.LogPosition));
		}

		return new(links, retired, dropped);
	}

	LinkFacts? ReadLink(string sourceEntityId, string targetEntityId) {
		const string sql =
			"""
			SELECT source_entity_id, target_entity_id, confidence, method, status, created_at, log_position
			FROM ldb.main.entity_links
			WHERE source_entity_id = $source_entity_id AND target_entity_id = $target_entity_id
			LIMIT 1
			""";

		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.Add(new DuckDBParameter("source_entity_id", sourceEntityId));
		command.Parameters.Add(new DuckDBParameter("target_entity_id", targetEntityId));

		using var reader = command.ExecuteReader();

		return reader.Read()
			? new(
				reader.GetString(0), reader.GetString(1), reader.GetDouble(2), reader.GetString(3),
				reader.GetString(4), reader.GetInt64(5), Convert.ToInt64(reader.GetValue(6)))
			: null;
	}

	List<LinkFacts> ReadPendingLinksTouching(string entityId) {
		const string sql =
			"""
			SELECT source_entity_id, target_entity_id, confidence, method, status, created_at, log_position
			FROM ldb.main.entity_links
			WHERE status = $status
			  AND (source_entity_id = $entity_id OR target_entity_id = $entity_id)
			ORDER BY created_at, source_entity_id
			""";

		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.Add(new DuckDBParameter("status", EntityLinkStatus.Pending));
		command.Parameters.Add(new DuckDBParameter("entity_id", entityId));

		var       links  = new List<LinkFacts>();
		using var reader = command.ExecuteReader();

		while (reader.Read())
			links.Add(new(
				reader.GetString(0), reader.GetString(1), reader.GetDouble(2), reader.GetString(3),
				reader.GetString(4), reader.GetInt64(5), Convert.ToInt64(reader.GetValue(6))));

		return links;
	}

	bool LinkExists(string sourceEntityId, string targetEntityId) {
		const string sql =
			"""
			SELECT count(*)
			FROM ldb.main.entity_links
			WHERE source_entity_id = $source_entity_id AND target_entity_id = $target_entity_id
			""";

		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.Add(new DuckDBParameter("source_entity_id", sourceEntityId));
		command.Parameters.Add(new DuckDBParameter("target_entity_id", targetEntityId));

		return (long)command.ExecuteScalar()! > 0;
	}

	// Matched-only MERGE on the pair, the shape the memory projection's folds use: the row must
	// already exist (it is the doubt being answered), and a status flip is not an insert.
	void SetStatus(string sourceEntityId, string targetEntityId, string status) {
		const string sql =
			"""
			MERGE INTO ldb.main.entity_links AS t
			USING (SELECT $source_entity_id AS source_entity_id, $target_entity_id AS target_entity_id) AS s
			ON t.source_entity_id = s.source_entity_id AND t.target_entity_id = s.target_entity_id
			WHEN MATCHED THEN UPDATE SET status = $status
			""";

		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.Add(new DuckDBParameter("source_entity_id", sourceEntityId));
		command.Parameters.Add(new DuckDBParameter("target_entity_id", targetEntityId));
		command.Parameters.Add(new DuckDBParameter("status", status));
		command.ExecuteNonQuery();
	}

	void DeleteLink(string sourceEntityId, string targetEntityId) {
		const string sql = "DELETE FROM ldb.main.entity_links WHERE source_entity_id = $source_entity_id AND target_entity_id = $target_entity_id";

		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.Add(new DuckDBParameter("source_entity_id", sourceEntityId));
		command.Parameters.Add(new DuckDBParameter("target_entity_id", targetEntityId));
		command.ExecuteNonQuery();
	}

	void DeleteEntity(string entityId) {
		const string sql = "DELETE FROM ldb.main.entities WHERE entity_id = $entity_id";

		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.Add(new DuckDBParameter("entity_id", entityId));
		command.ExecuteNonQuery();
	}

	// The link ledger's row as the executor reads it: EntityLinkRow drops log_position, which a
	// repointed row has to carry forward.
	sealed record LinkFacts(
		string Source, string Target, double Confidence, string Method, string Status, long CreatedAt, long LogPosition);

	sealed record Repoint(List<LinkWrite> Links, List<(string Source, string Target)> Retired, int Dropped);
}
