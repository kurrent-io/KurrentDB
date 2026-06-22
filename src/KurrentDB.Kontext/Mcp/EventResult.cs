// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Mcp;

public record EventResult {
	public long Id => PreparePosition;

	public required string Stream { get; init; }

	public long? EventNumber { get; init; }

	public long? CommitPosition { get; init; }

	public required long PreparePosition { get; init; }

	public string? EventType { get; init; }

	public DateTime? Timestamp { get; init; }

	public float? Score { get; init; }

	public ReadOnlyMemory<byte> Data { get; init; }

	public ReadOnlyMemory<byte> Metadata { get; init; }

	public bool AccessDenied { get; init; }

	public EventResult Redact() => this with { Data = default, Metadata = default, AccessDenied = true };
}