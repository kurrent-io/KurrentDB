// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable UseCollectionExpression

using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Entities.Extraction;

public static partial class EntityExtractor {
    /// <summary>
    /// Runs every extractor and merges their entities by normalized surface form: the stronger
    /// opinion wins, ties keep the first, so extractor order is priority order. A failing
    /// extractor costs coverage, never the batch. Survivors come back filtered, in content order.
    /// </summary>
    public sealed class Pipeline(
        IReadOnlyList<IEntityExtractor> extractors,
        ILogger<Pipeline> logger,
        PipelineOptions? options = null
    ) : IEntityExtractor {
        readonly PipelineOptions _options = options ?? new PipelineOptions();

        public async ValueTask<IReadOnlyList<ExtractedEntity>> ExtractAsync(string content, CancellationToken ct = default) {
            var merged = new OrderedDictionary<string, ExtractedEntity>();

            foreach (var extractor in extractors) {
                IReadOnlyList<ExtractedEntity> entities;

                try {
                    entities = await extractor.ExtractAsync(content, ct).ConfigureAwait(false);
                } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                    throw;
                } catch (Exception ex) {
                    logger.LogExtractorFailed(ex, extractor.GetType().Name);
                    continue;
                }

                foreach (var entity in Prepare(entities)) {
                    var name = EntityId.Normalize(entity.Text);

                    if (!merged.TryGetValue(name, out var existing))
                        merged.Add(name, entity);
                    else if (entity.Outranks(existing))
                        merged[name] = entity;
                }
            }

            return merged.Values
                .Where(entity => SpanFilter.Accepts(entity.Text))
                .OrderBy(entity => FirstAppearance(content, entity.Text))
                .ToList();

            // Sort it in the order the text mentions them
            static int FirstAppearance(string content, string text) {
	            var index = content.IndexOf(text, StringComparison.OrdinalIgnoreCase);
	            return index < 0 ? int.MaxValue : index;
            }
        }

        IEnumerable<ExtractedEntity> Prepare(IReadOnlyList<ExtractedEntity> entities) =>
            _options.SplitCoordinatedSpans
                ? entities.SelectMany(SpanSplitter.Split)
                : entities;
    }

    /// <summary>The merge's knobs.</summary>
    public sealed class PipelineOptions {
        /// <summary>
        /// Whether a coordinated span is broken into the entities it names before merging:
        /// "counseling and support groups" becomes both. Flat NER returns one span per range, so
        /// without this the parts are lost at extraction and nothing downstream can recover them.
        /// </summary>
        public bool SplitCoordinatedSpans { get; set; } = true;
    }
}

static partial class EntityExtractorPipelineLogMessages {
    [LoggerMessage(LogLevel.Warning, "Entity extractor {Extractor} failed; continuing with the remaining extractors")]
    internal static partial void LogExtractorFailed(this ILogger logger, Exception ex, string extractor);
}
