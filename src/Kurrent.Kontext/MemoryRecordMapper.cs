// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Edges.Mcp.Model;

namespace Kurrent.Kontext;

/// <summary>
/// Maps between the canonical contract messages and the <see cref="MemoryRecord"/> storage row.
/// Enums travel as their proto numeric values; evidence travels as protobuf bytes (with the cited
/// memory ids flattened out for the retract cascade); tags travel in TagParser's canonical encoded
/// form so containment filters work on a plain string list.
/// </summary>
static class MemoryRecordMapper {
	#region ->> Tags <<-

	public static string EncodeTag(Contracts.Tag tag) {
		var scope = TagParser.Sanitize(tag.Scope);
		var value = TagParser.Sanitize(tag.Value);
		return scope.Length == 0 ? value : $"{scope}:{value}";
	}

	public static Contracts.Tag DecodeTag(string encoded) {
		var (value, scope) = TagParser.Parse(encoded);
		return new() { Scope = scope, Value = value };
	}

	#endregion // Tags

	#region ->> Write (command -> record) <<-

	public static MemoryRecord ToRecord(Contracts.Memory memory, string memoryId, DateTimeOffset now) => new() {
		MemoryId       = memoryId,
		MemoryType     = (int)memory.MemoryType,
		Content        = memory.Content,
		Importance     = (int)memory.Importance,
		Sentiment      = (int)memory.Sentiment,
		Urgency        = (int)memory.Urgency,
		Tags           = memory.Tags.Select(EncodeTag).Distinct().ToList(),
		Evidence       = memory.Evidence?.ToByteArray() ?? [],
		CitedMemoryIds = CitedMemoryIds(memory.Evidence),
		Supersedes     = memory.Supersedes.ToList(),
		ValidityStart  = memory.Validity?.PerceivedStart?.ToDateTimeOffset(),
		ValidityEnd    = memory.Validity?.PerceivedEnd?.ToDateTimeOffset(),
		RetainedAt     = now,
		LastAccessedAt = now,
	};

	static List<string> CitedMemoryIds(Contracts.Evidence? evidence) =>
		evidence is null
			? []
			: evidence.Citations
				.Where(c => c.CitedCase == Contracts.Evidence.Types.Citation.CitedOneofCase.Memory)
				.Select(c => c.Memory.Id)
				.Distinct()
				.ToList();

	#endregion // Write (command -> record)

	#region ->> Read (record -> read models) <<-

	public static Contracts.StoredMemory ToStoredMemory(MemoryRecord record) {
		var stored = new Contracts.StoredMemory {
			MemoryId       = record.MemoryId,
			MemoryType     = (Contracts.MemoryType)record.MemoryType,
			Content        = record.Content,
			Importance     = (Contracts.MemoryImportance)record.Importance,
			Sentiment      = (Contracts.MemorySentiment)record.Sentiment,
			Urgency        = (Contracts.MemoryUrgency)record.Urgency,
			RetainedAt     = Timestamp.FromDateTimeOffset(record.RetainedAt),
			LastAccessedAt = Timestamp.FromDateTimeOffset(record.LastAccessedAt),
			SupersededBy   = record.SupersededBy,
		};

		stored.Tags.AddRange(record.Tags.Select(DecodeTag));
		stored.Supersedes.AddRange(record.Supersedes);

		if (record.Evidence.Length > 0)
			stored.Evidence = Contracts.Evidence.Parser.ParseFrom(record.Evidence);

		if (record.ValidityStart is { } start) {
			stored.Validity = new() { PerceivedStart = Timestamp.FromDateTimeOffset(start) };
			if (record.ValidityEnd is { } end)
				stored.Validity.PerceivedEnd = Timestamp.FromDateTimeOffset(end);
		}

		if (record.RetractedAt is { } retractedAt)
			stored.RetractedAt = Timestamp.FromDateTimeOffset(retractedAt);

		if (record.SupersededAt is { } supersededAt)
			stored.SupersededAt = Timestamp.FromDateTimeOffset(supersededAt);

		return stored;
	}

	public static Contracts.RecallResponse.Types.RecalledMemory.Types.LeanMemory ToLeanMemory(MemoryRecord record) {
		var lean = new Contracts.RecallResponse.Types.RecalledMemory.Types.LeanMemory {
			MemoryId   = record.MemoryId,
			MemoryType = (Contracts.MemoryType)record.MemoryType,
			Content    = record.Content,
			Importance = (Contracts.MemoryImportance)record.Importance,
			RetainedAt = Timestamp.FromDateTimeOffset(record.RetainedAt),
		};

		lean.Tags.AddRange(record.Tags.Select(DecodeTag));
		return lean;
	}

	#endregion // Read (record -> read models)
}
