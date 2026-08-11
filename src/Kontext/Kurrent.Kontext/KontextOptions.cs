// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext;

/// <summary>
/// Kontext's full configuration surface — the ONE owner of the <c>KurrentDB:Kontext</c> section.
/// Every sub-concern nests under it (embeddings today; records indexing and maintenance slot in
/// beside it), so no other class binds the section or any part of it. A plain mutable settings
/// class so it binds from configuration.
/// </summary>
public sealed class KontextOptions {
    public const string SectionName = "KurrentDB:Kontext";

    public bool Enabled { get; set; }

    /// <summary>The storage root. Empty means derived by the host (index directory + "kontext").</summary>
    public string Path { get; set; } = "";

    public KontextEmbeddingsOptions Embeddings { get; set; } = new();
}
