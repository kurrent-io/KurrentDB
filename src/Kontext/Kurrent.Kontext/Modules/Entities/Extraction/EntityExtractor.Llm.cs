// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Modules.Entities.Extraction;

public static partial class EntityExtractor {
    /// <summary>
    /// Asks a chat model for the entities in the content as strict JSON, one call per memory,
    /// temperature 0. Types fold into the canonical vocabulary. A reply that is not the
    /// asked-for JSON throws, the pipeline logs it and carries on, so garbage degrades coverage
    /// instead of silently reading as "no entities".
    /// </summary>
    public sealed class Llm(Llm.Options options) : IEntityExtractor {
        static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

        readonly IChatClient _chat = options.Chat
            ?? throw new InvalidOperationException("No chat client configured. Set Options.Chat when creating the extractor.");

        /// <summary>Creates the extractor from pre-built options — the config-binding door.</summary>
        public static Llm Create(Options options) => new(options);

        /// <summary>Creates the extractor over default options, tuned via the callback.</summary>
        public static Llm Create(Action<Options> configure) {
            var options = new Options();
            configure(options);
            return Create(options);
        }

        public async ValueTask<IReadOnlyList<ExtractedEntity>> ExtractAsync(string content, CancellationToken ct = default) {
            var chatOptions = new ChatOptions {
                Temperature     = 0,
                MaxOutputTokens = options.MaxOutputTokens,
                ResponseFormat  = ChatResponseFormat.Json
            };

            var response = await _chat.GetResponseAsync(options.BuildPrompt(content), chatOptions, ct).ConfigureAwait(false);

            return Parse(response.Text);
        }

        static IReadOnlyList<ExtractedEntity> Parse(string? answer) {
            ExtractionPayload? payload;

            try {
                payload = JsonSerializer.Deserialize<ExtractionPayload>(Unfence(answer), PayloadJson);
            } catch (JsonException ex) {
                throw new InvalidOperationException($"The LLM extractor's reply is not the asked-for JSON: {Head(answer)}", ex);
            }

            if (payload?.Entities is not { } entities)
                return [];

            var extracted = new List<ExtractedEntity>(entities.Count);
            var seen      = new HashSet<string>();

            foreach (var entity in entities) {
                if (entity.Name?.Trim() is not { Length: > 0 } text || !seen.Add(EntityId.Normalize(text)))
                    continue;

                extracted.Add(new(text, EntityTypes.Normalize(entity.Type), Math.Clamp(entity.Confidence ?? 1.0, 0, 1)));
            }

            return extracted;

            // Models fence JSON in markdown despite instructions — strip a leading ```/```json line
            // and a trailing ``` so the fence never poisons the parse.
            static string Unfence(string? answer) {
                var trimmed = answer?.Trim() ?? "";

                if (!trimmed.StartsWith("```", StringComparison.Ordinal))
                    return trimmed;

                var opening = trimmed.IndexOf('\n');
                var closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);

                return opening < 0 || closing <= opening ? trimmed : trimmed[(opening + 1)..closing].Trim();
            }

            static string Head(string? answer) {
                var trimmed = answer?.Trim() ?? "<empty>";

                return trimmed.Length <= 120 ? trimmed : trimmed[..120] + "…";
            }
        }

        sealed record ExtractionPayload(List<ExtractedEntityPayload>? Entities);

        sealed record ExtractedEntityPayload(string? Name, string? Type, double? Confidence);

        public sealed class Options {
            /// <summary>The chat client the extractor asks. Required.</summary>
            public IChatClient? Chat { get; set; }

            /// <summary>Cap on the model's reply length; entity lists are short but not single-token.</summary>
            public int MaxOutputTokens { get; set; } = 1024;

            /// <summary>
            /// Builds the extraction prompt. Default asks for the canonical vocabulary and allows
            /// a more precise lowercase noun when nothing fits; override to change the rubric or
            /// add few-shot examples.
            /// </summary>
            public Func<string, string> BuildPrompt { get; set; } = content =>
                $$"""
                  Extract the named entities from the text.

                  Entity types:
                  - person: individuals, mentioned by name or role
                  - organization: companies, teams, institutions, groups
                  - location: places, addresses, geographic areas, landmarks
                  - event: incidents, meetings, releases, things that happened
                  - object: physical or digital items (projects, documents, systems, products)

                  Return only a JSON object with this structure:
                  {"entities": [{"name": "entity name", "type": "person", "confidence": 0.9}]}

                  - name: the surface form exactly as it appears in the text
                  - type: one of the types above, or a more precise lowercase noun when none fits
                  - confidence: 0.0 to 1.0, certainty that this names a real entity

                  Text:
                  {{content}}
                  """;
        }
    }
}
