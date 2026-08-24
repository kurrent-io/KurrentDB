// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings.GlinerOnnx;

namespace Kurrent.Kontext.Entities.Extraction;

public static partial class EntityExtractor {
    /// <summary>
    /// Zero-shot local NER: GLiNER scores every span of the content against the label set, so a
    /// new entity type costs a label string, not a retrain. Labels are normalized on the way out,
    /// so a custom label list stays consistent with the pipeline's merge.
    /// </summary>
    public sealed class Gliner(GlinerOnnxEntityRecognizer recognizer, IReadOnlyList<string> labels) : IEntityExtractor {
        /// <summary>Creates the extractor over the extraction vocabulary.</summary>
        public static Gliner Create(GlinerOnnxEntityRecognizer recognizer) =>
            new(recognizer, EntityTypes.ExtractionLabels);

        public ValueTask<IReadOnlyList<ExtractedEntity>> ExtractAsync(string content, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            var recognized = recognizer.Recognize(content, labels);

            var entities = new List<ExtractedEntity>(recognized.Count);
            var seen     = new HashSet<string>();

            foreach (var span in recognized)
                if (seen.Add(EntityId.Normalize(span.Text)))
                    entities.Add(new(span.Text, EntityTypes.Normalize(span.Label), span.Score));

            return ValueTask.FromResult<IReadOnlyList<ExtractedEntity>>(entities);
        }
    }
}
