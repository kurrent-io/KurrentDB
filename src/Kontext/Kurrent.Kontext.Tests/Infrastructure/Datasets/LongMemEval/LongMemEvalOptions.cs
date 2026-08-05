// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Tests.Infrastructure.Datasets.LongMemEval;

public class LongMemEvalOptions {
    /// <summary>
    /// Assistant turns are skipped by default: the corpus is seeded as what the USER said.
    /// Knowledge-update evidence turns are always emitted regardless of role, or the
    /// supersession chain would silently lose its links.
    /// </summary>
    public bool IncludeAssistantTurns { get; set; }

    /// <summary>Cap on dataset instances to read; null reads all of them.</summary>
    public int? MaxInstances { get; set; }

    /// <summary>Tag scope and memory-id prefix, so a test run can slice its own seed data.</summary>
    public string TagScope { get; set; } = "lme";
}
