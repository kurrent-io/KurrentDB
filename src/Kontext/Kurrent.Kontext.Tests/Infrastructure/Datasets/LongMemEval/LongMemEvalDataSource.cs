// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;

namespace Kurrent.Kontext.Tests.Infrastructure.Datasets.LongMemEval;

/// <summary>
/// Maps the LongMemEval oracle dataset (https://hf.co/datasets/xiaowu0162/longmemeval-cleaned,
/// MIT) into memory events: one <c>MemoryRetained</c> per conversation turn, stamped with the
/// session's date. Knowledge-update instances chain their evidence turns through
/// <c>Memory.supersedes</c>, so the dataset's "the fact changed" structure survives into the
/// read model. Memory ids derive from the source (<c>{scope}:{question_id}:{session}:{turn}</c>),
/// so reseeding the same file is idempotent.
/// </summary>
public sealed partial class LongMemEvalDataSource(string path, LongMemEvalOptions? options = null) : IKontextTestDataSource {
    LongMemEvalOptions Options { get; } = options ?? new();

    public async IAsyncEnumerable<Contracts.MemoriesRetained> ReadEvents([EnumeratorCancellation] CancellationToken ct = default) {
        await using var file = File.OpenRead(path);

        var instances = 0;
        await foreach (var instance in JsonSerializer.DeserializeAsyncEnumerable(file, LongMemEvalJson.Default.LongMemEvalInstance, ct).ConfigureAwait(false)) {
            if (instance is null)
                continue;

            if (Options.MaxInstances is { } max && ++instances > max)
                yield break;

            foreach (var retained in MapInstance(instance))
                yield return retained;
        }
    }

    IEnumerable<Contracts.MemoriesRetained> MapInstance(LongMemEvalInstance instance) {
        // haystack_sessions arrive in arbitrary date order; emit chronologically so a
        // knowledge-update successor is always yielded after the memory it supersedes.
        var sessions = instance.HaystackSessions
            .Select((turns, index) => (Turns: turns, Index: index, At: ParseSessionDate(instance.HaystackDates[index])))
            .OrderBy(s => s.At)
            .ThenBy(s => s.Index);

        var chainEvidence = instance.QuestionType == "knowledge-update";

        string? previousEvidenceId = null;

        foreach (var (turns, sessionIndex, at) in sessions) {
            var retainedAt = Timestamp.FromDateTimeOffset(at);

            foreach (var (turnIndex, turn) in turns.Index()) {
                if (string.IsNullOrWhiteSpace(turn.Content))
                    continue;

                var isEvidence = chainEvidence && turn.HasAnswer;

                // Evidence turns bypass the role filter: dropping one would silently break the
                // supersession chain, which is the dataset's most valuable structure.
                if (!isEvidence && turn.Role != "user" && !Options.IncludeAssistantTurns)
                    continue;

                var memoryId = $"{Options.TagScope}:{instance.QuestionId}:{sessionIndex}:{turnIndex}";

                var memory = new Contracts.Memory {
                    // Hearsay by the contract's own definition: an unverified claim,
                    // kept low-trust so it cannot launder itself into fact.
                    MemoryType = Contracts.MemoryType.Hearsay,
                    Content    = turn.Content,
                    Tags       = { new Contracts.Tag { Scope = Options.TagScope, Value = instance.QuestionId } }
                };

                if (isEvidence) {
                    if (previousEvidenceId is not null)
                        memory.Supersedes.Add(previousEvidenceId);

                    previousEvidenceId = memoryId;
                }

                // One turn is one retain call, so each event carries a single-memory batch. The id
                // rides on the event, not the memory body: the write shape carries none because the
                // server mints it (see resources.proto Memory).
                yield return new() {
                    Memories   = { new Contracts.MemoriesRetained.Types.RetainedMemory { MemoryId = memoryId, Memory = memory } },
                    RetainedAt = retainedAt,
                };
            }
        }
    }

    // Session dates arrive as "2023/04/10 (Mon) 14:47"; the parenthesized day name is
    // redundant and not trusted, so it is stripped before parsing. Dates carry no zone —
    // treated as UTC.
    static DateTimeOffset ParseSessionDate(string raw) =>
        DateTimeOffset.ParseExact(
            SessionDayName().Replace(raw, " ").Trim(), "yyyy/MM/dd HH:mm",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    [GeneratedRegex(@"\s*\([^)]*\)\s*")]
    private static partial Regex SessionDayName();
}

sealed class LongMemEvalInstance {
    public string QuestionId { get; set; } = "";
    public string QuestionType { get; set; } = "";
    public List<string> HaystackDates { get; set; } = [];
    public List<List<LongMemEvalTurn>> HaystackSessions { get; set; } = [];
}

sealed class LongMemEvalTurn {
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public bool HasAnswer { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(LongMemEvalInstance))]
partial class LongMemEvalJson : JsonSerializerContext;
