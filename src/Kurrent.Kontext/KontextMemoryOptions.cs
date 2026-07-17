// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext;

/// <summary>
/// Host-facing configuration for <see cref="KontextMemory"/>. A mutable settings class by design —
/// config binding does not cope with records.
/// </summary>
public sealed class KontextMemoryOptions {
	/// <summary>Opt-in write-behind buffering for reconsolidation touches. Disabled by default.</summary>
	public TouchBufferOptions TouchBuffer { get; set; } = new();
}

/// <summary>
/// Settings for the opt-in touch buffer. Reconsolidation touches — the recency-clock refresh recall
/// and reclaim perform on every memory they return — are the service's one loss-tolerant write: a
/// clock, not data. Buffered touches coalesce per memory id (last touch wins) and flush off the
/// request path when <see cref="BatchSize"/> distinct memories are pending, when the oldest pending
/// touch has waited <see cref="BatchWait"/>, or when the service disposes. The trade-off is
/// visibility: a concurrently read record may show a slightly stale last-accessed time, and a crash
/// loses pending refreshes — never memory data. Retains and retracts are never buffered.
/// </summary>
public sealed class TouchBufferOptions {
	/// <summary>Buffering is opt-in; when false (the default) touches are written before the operation returns.</summary>
	public bool Enabled { get; set; }

	/// <summary>Number of pending distinct memories that triggers an immediate flush.</summary>
	public int BatchSize { get; set; } = 256;

	/// <summary>Longest a pending touch waits before the buffer flushes regardless of size.</summary>
	public TimeSpan BatchWait { get; set; } = TimeSpan.FromMilliseconds(500);
}
