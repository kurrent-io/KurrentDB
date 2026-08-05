// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Tests.Infrastructure.Datasets.LongMemEval;

namespace Kurrent.Kontext.Tests.Infrastructure.Datasets;

public class LongMemEvalDataSourceTests {
    // One knowledge-update instance whose sessions are deliberately listed LATER-DATE-FIRST:
    // the mapper must reorder chronologically or the supersession chain inverts.
    const string KnowledgeUpdateInstance =
        """
        [{
          "question_id": "q1",
          "question_type": "knowledge-update",
          "question": "unused",
          "answer": "unused",
          "haystack_dates": ["2023/05/20 (Sat) 10:00", "2023/04/10 (Mon) 14:47"],
          "haystack_session_ids": ["s2", "s1"],
          "haystack_sessions": [
            [ {"role": "user", "content": "updated fact", "has_answer": true} ],
            [ {"role": "user", "content": "original fact", "has_answer": true},
              {"role": "assistant", "content": "assistant reply"} ]
          ]
        }]
        """;

    [Test]
    public async ValueTask maps_turns_chronologically_and_chains_evidence_through_supersedes() {
        // Arrange
        using var dataset = new TempDataset(KnowledgeUpdateInstance);

        var expectedOriginalAt = new DateTimeOffset(2023, 4, 10, 14, 47, 0, TimeSpan.Zero);
        var expectedUpdatedAt  = new DateTimeOffset(2023, 5, 20, 10, 0, 0, TimeSpan.Zero);

        // Act
        var events = await new LongMemEvalDataSource(dataset.Path).ReadEvents().ToListAsync();

        // Assert — the April session comes first despite being listed second in the file.
        await Assert.That(events).HasCount().EqualTo(2);

        var original = events[0];
        var updated  = events[1];

        await Assert.That(original.Memories[0].MemoryId).IsEqualTo("lme:q1:1:0");
        await Assert.That(original.Memories[0].Memory.Content).IsEqualTo("original fact");
        await Assert.That(original.Memories[0].Memory.Supersedes).IsEmpty();
        await Assert.That(original.RetainedAt.ToDateTimeOffset()).IsEqualTo(expectedOriginalAt);

        // Assert — the later evidence supersedes the earlier one, and only appears after it.
        await Assert.That(updated.Memories[0].MemoryId).IsEqualTo("lme:q1:0:0");
        await Assert.That(updated.Memories[0].Memory.Content).IsEqualTo("updated fact");
        await Assert.That(updated.Memories[0].Memory.Supersedes).Contains("lme:q1:1:0");
        await Assert.That(updated.RetainedAt.ToDateTimeOffset()).IsEqualTo(expectedUpdatedAt);
    }

    [Test]
    public async ValueTask maps_turns_as_low_trust_hearsay_tagged_by_question() {
        // Arrange
        using var dataset = new TempDataset(KnowledgeUpdateInstance);

        var expectedTag = new Contracts.Tag { Scope = "lme", Value = "q1" };

        // Act
        var events = await new LongMemEvalDataSource(dataset.Path).ReadEvents().ToListAsync();

        // Assert
        foreach (var retained in events) {
            await Assert.That(retained.Memories[0].Memory.MemoryType).IsEqualTo(Contracts.MemoryType.Hearsay);
            await Assert.That(retained.Memories[0].Memory.Tags).Contains(expectedTag);
        }
    }

    [Test]
    public async ValueTask excludes_assistant_turns_unless_opted_in() {
        // Arrange
        using var dataset = new TempDataset(KnowledgeUpdateInstance);

        // Act
        var defaults = await new LongMemEvalDataSource(dataset.Path).ReadEvents().ToListAsync();
        var included = await new LongMemEvalDataSource(dataset.Path, new() { IncludeAssistantTurns = true })
            .ReadEvents().ToListAsync();

        // Assert — the assistant reply only appears when opted in.
        await Assert.That(defaults.Select(e => e.Memories[0].Memory.Content)).DoesNotContain("assistant reply");
        await Assert.That(included.Select(e => e.Memories[0].Memory.Content)).Contains("assistant reply");
        await Assert.That(included).HasCount().EqualTo(3);
    }

    [Test]
    public async ValueTask honors_max_instances() {
        // Arrange — two single-turn instances; the cap must stop after the first.
        const string twoInstances =
            """
            [
              { "question_id": "q1", "question_type": "single-session-user",
                "haystack_dates": ["2023/04/10 (Mon) 14:47"],
                "haystack_sessions": [[ {"role": "user", "content": "first"} ]] },
              { "question_id": "q2", "question_type": "single-session-user",
                "haystack_dates": ["2023/04/11 (Tue) 09:00"],
                "haystack_sessions": [[ {"role": "user", "content": "second"} ]] }
            ]
            """;

        using var dataset = new TempDataset(twoInstances);

        // Act
        var events = await new LongMemEvalDataSource(dataset.Path, new() { MaxInstances = 1 })
            .ReadEvents().ToListAsync();

        // Assert
        await Assert.That(events).HasCount().EqualTo(1);
        await Assert.That(events[0].Memories[0].Memory.Content).IsEqualTo("first");
    }

    sealed class TempDataset : IDisposable {
        public TempDataset(string json) {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lme-{Guid.NewGuid():N}.json");
            File.WriteAllText(Path, json);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
