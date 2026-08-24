// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Modules.Entities;

/// <summary>A catalog entity a cheaper tier surfaced but would not merge on its own.</summary>
public sealed record EntityCandidate(string EntityId, string Alias, double Similarity);

/// <summary>One name the cheaper tiers left unresolved, with the entities they thought it might be.</summary>
public sealed record Disambiguation(EntityKey Key, string Text, IReadOnlyList<EntityCandidate> Candidates);

/// <summary>
/// Decides, for a name no deterministic tier would merge, which candidate it actually is. The last
/// tier before a name becomes a new entity, and the only one that can see past spelling: "film
/// club" and "cinema society" share no stem, no prefix and no rule.
/// </summary>
/// <remarks>
/// Absence from the result is an abstention, and abstaining is always allowed — an unresolved name
/// costs one duplicate entity, a wrong merge fuses two things forever and nothing splits them back
/// apart. Callers treat "not in the result" exactly as they treat "no candidate matched".
/// </remarks>
public interface IEntityDisambiguator {
    ValueTask<IReadOnlyDictionary<EntityKey, string>> ResolveAsync(
        IReadOnlyCollection<Disambiguation> pending, CancellationToken ct = default
    );
}

/// <summary>
/// Asks a chat model which candidate each name is, one call for the whole batch, temperature 0.
/// A reply that is not the asked-for JSON abstains for every name in the batch rather than
/// throwing: the tier exists to add merges the deterministic tiers cannot make, so its failure
/// must cost recall and never correctness.
/// </summary>
public sealed class EntityDisambiguator(EntityDisambiguatorOptions options) : IEntityDisambiguator {
    static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    readonly IChatClient _chat = options.Chat
        ?? throw new InvalidOperationException($"{nameof(EntityDisambiguatorOptions)}.{nameof(EntityDisambiguatorOptions.Chat)} is required.");

    /// <summary>Creates the disambiguator from pre-built options — the config-binding door.</summary>
    public static EntityDisambiguator Create(EntityDisambiguatorOptions options) => new(options);

    /// <summary>Creates the disambiguator over default options, tuned via <paramref name="configure"/>.</summary>
    public static EntityDisambiguator Create(Action<EntityDisambiguatorOptions> configure) {
        var options = new EntityDisambiguatorOptions();
        configure(options);
        return Create(options);
    }

    public async ValueTask<IReadOnlyDictionary<EntityKey, string>> ResolveAsync(
        IReadOnlyCollection<Disambiguation> pending, CancellationToken ct = default
    ) {
        if (pending.Count == 0)
            return new Dictionary<EntityKey, string>();

        // Positional ids, so the model never has to echo an entity id back and cannot invent one:
        // it answers with a number, and the number indexes a candidate this method already holds.
        var numbered = pending.Select((item, index) => (Index: index, Item: item)).ToList();

        var chatOptions = new ChatOptions {
            Temperature     = 0,
            MaxOutputTokens = options.MaxOutputTokens,
            ResponseFormat  = ChatResponseFormat.Json,
        };

        var prompt = options.BuildPrompt(Render(numbered));

        ChatResponse response;

        try {
            response = await _chat.GetResponseAsync(prompt, chatOptions, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception) {
            return new Dictionary<EntityKey, string>();
        }

        return Choose(numbered, Parse(response.Text));
    }

    static string Render(List<(int Index, Disambiguation Item)> numbered) {
        var text = new StringBuilder();

        foreach (var (index, item) in numbered) {
            text.Append("name ").Append(index).Append(": \"").Append(item.Text)
                .Append("\" (type: ").Append(item.Key.EntityType).AppendLine(")");

            for (var candidate = 0; candidate < item.Candidates.Count; candidate++)
                text.Append("  candidate ").Append(candidate).Append(": \"")
                    .Append(item.Candidates[candidate].Alias).AppendLine("\"");
        }

        return text.ToString();
    }

    // Only a choice that indexes a candidate the caller offered survives: a hallucinated index, a
    // name index that was never asked about, and the explicit -1 abstention all fall through to
    // absence, which the resolver reads as "no match".
    static Dictionary<EntityKey, string> Choose(
        List<(int Index, Disambiguation Item)> numbered, List<ChoicePayload> choices
    ) {
        var byIndex  = numbered.ToDictionary(entry => entry.Index, entry => entry.Item);
        var resolved = new Dictionary<EntityKey, string>();

        foreach (var choice in choices) {
            if (choice.Name is not { } name || choice.Candidate is not { } candidate)
                continue;

            if (!byIndex.TryGetValue(name, out var item) || candidate < 0 || candidate >= item.Candidates.Count)
                continue;

            resolved[item.Key] = item.Candidates[candidate].EntityId;
        }

        return resolved;
    }

    static List<ChoicePayload> Parse(string? answer) {
        try {
            return JsonSerializer.Deserialize<ChoicesPayload>(Unfence(answer), PayloadJson)?.Choices ?? [];
        } catch (JsonException) {
            return [];
        }

        // Models fence JSON in markdown despite instructions — strip a leading ```/```json line and
        // a trailing ``` so the fence never poisons the parse.
        static string Unfence(string? answer) {
            var trimmed = answer?.Trim() ?? "";

            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
                return trimmed;

            var opening = trimmed.IndexOf('\n');
            var closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);

            return opening < 0 || closing <= opening ? trimmed : trimmed[(opening + 1)..closing].Trim();
        }
    }

    sealed record ChoicesPayload(List<ChoicePayload>? Choices);

    sealed record ChoicePayload(int? Name, int? Candidate);
}

public sealed class EntityDisambiguatorOptions {
    /// <summary>The chat client the disambiguator asks. Required.</summary>
    public IChatClient? Chat { get; set; }

    /// <summary>Cap on the model's reply length; the answer is one small object per name.</summary>
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>
    /// Builds the disambiguation prompt from the rendered name/candidate list. Default states the
    /// rubric that keeps the tier safe — same real thing only, abstain when unsure — and shows the
    /// three cases that matter: a paraphrase, a narrower thing, and a shared word.
    /// </summary>
    public Func<string, string> BuildPrompt { get; set; } = listing =>
        $$"""
          Each name below was found in a memory. For each one, decide whether it refers to the same
          real-world thing as one of its candidates, which are names already in the catalog.

          Two names are the same thing only if they name the SAME individual thing. Different things
          that merely share words, a category, or a purpose are NOT the same. Answer -1 whenever no
          candidate matches or you are unsure — leaving a name unmatched is always safe.

          Return only a JSON object with this structure:
          {"choices": [{"name": 0, "candidate": 1}, {"name": 1, "candidate": -1}]}

          - name: the number of the name being decided
          - candidate: the number of the candidate it is, or -1 for none

          Examples:
          - "film club" vs candidate "cinema society" -> the same club, different wording
          - "farmers market" vs candidate "downtown farmers market" -> the same place, one name abbreviated
          - "pottery project" vs candidate "pottery class" -> NOT the same: a project and a class
          - "Java" (the language) vs candidate "Java" (the island) -> NOT the same: one word, two things

          {{listing}}
          """;
}
