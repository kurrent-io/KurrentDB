// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Entities.Extraction;

namespace Kurrent.Kontext.Entities;

using Contracts = Kurrent.Kontext.Contracts.V3.Entities;

/// <summary>
/// The one place a mention is shaped for the entities contract. Ingest and the benchmarks that
/// stand in for it both map through here, so neither can drift from what a mention says.
/// </summary>
public static class EntityResolutionMapping {
    extension(ExtractedEntity source) {
        public Contracts.EntityMention ToContract(ResolvedEntity resolved) =>
            new() {
                SpanText   = source.Text,
                EntityId   = resolved.EntityId,
                EntityType = source.EntityType,
                Confidence = resolved.Confidence,
                ResolvedBy = resolved.Method,
            };
    }
}
