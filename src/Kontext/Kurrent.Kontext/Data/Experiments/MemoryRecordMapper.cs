// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Edges.Mcp.Model;

namespace Kurrent.Kontext.Data;

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

    #endregion

    #region ->> Write (stored -> record) <<-

    // The lifecycle flags derive from timestamp presence: the workflows always stamp RetractedAt /
    // SupersededAt when they retract or supersede, so presence IS the flag.
    public static MemoryRecord ToRecord(Contracts.StoredMemory stored) =>
        new() {
            MemoryId       = stored.MemoryId,
            MemoryType     = (int)stored.MemoryType,
            Content        = stored.Content,
            Importance     = (int)stored.Importance,
            Sentiment      = (int)stored.Sentiment,
            Urgency        = (int)stored.Urgency,
            Tags           = stored.Tags.Select(EncodeTag).Distinct().ToList(),
            Evidence       = stored.Evidence?.ToByteArray() ?? [],
            CitedMemoryIds = CitedMemoryIds(stored.Evidence),
            Supersedes     = stored.Supersedes.ToList(),
            ValidityStart  = stored.Validity?.PerceivedStart?.ToDateTimeOffset(),
            ValidityEnd    = stored.Validity?.PerceivedEnd?.ToDateTimeOffset(),
            RetainedAt     = stored.RetainedAt.ToDateTimeOffset(),
            LastAccessedAt = stored.LastAccessedAt.ToDateTimeOffset(),
            IsRetracted    = stored.RetractedAt is not null,
            RetractedAt    = stored.RetractedAt?.ToDateTimeOffset(),
            IsSuperseded   = stored.SupersededAt is not null,
            SupersededAt   = stored.SupersededAt?.ToDateTimeOffset(),
            SupersededBy   = stored.SupersededBy,
        };

    static List<string> CitedMemoryIds(Contracts.Evidence? evidence) =>
        evidence is null
            ? []
            : evidence
                .Citations
                .Where(c => c.CitedCase == Contracts.Evidence.Types.Citation.CitedOneofCase.Memory)
                .Select(c => c.Memory.Id)
                .Distinct()
                .ToList();

    #endregion

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

    #endregion
}