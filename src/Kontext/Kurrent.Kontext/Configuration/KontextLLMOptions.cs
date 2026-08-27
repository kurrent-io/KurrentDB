// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext;

/// <summary>
/// The LLM block: any OpenAI-compatible endpoint (OpenAI, Ollama, vLLM, LM Studio).
/// Entity extraction requires it — a missing key or model is a startup error, never a thinner
/// pipeline. A plain mutable settings class so it binds from configuration.
/// </summary>
public sealed class KontextLLMOptions {
    /// <summary>Required against api.openai.com; null is accepted by self-hosted OpenAI-compatible servers.</summary>
    public string? ApiKey {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>The model to call.</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>An OpenAI-compatible base URL. Null means api.openai.com.</summary>
    public Uri? Endpoint { get; set; }
}
