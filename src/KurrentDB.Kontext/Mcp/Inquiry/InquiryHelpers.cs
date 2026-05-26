// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Mcp.Inquiry;

/// <summary>Shared helpers used by the inquiry-scoped MCP tools.</summary>
internal static class InquiryHelpers {
	// Matches the "count ≤ 32 per call" contract documented on the inq_read tool.
	public const int MaxEventRange = 32;

	/// <summary>
	/// Validate an <see cref="EventRef"/>. Returns null on success, an error message otherwise.
	/// Guards against negative event numbers, non-positive counts, and counts exceeding <see cref="MaxEventRange"/>.
	/// </summary>
	public static string? ValidateRange(EventRef r) {
		if (r.EventNumber < 0)
			return "event number must be non-negative.";

		if (r.Count < 1)
			return "count must be at least 1.";

		if (r.Count > MaxEventRange)
			return $"count exceeds {MaxEventRange}.";

		return null;
	}

	public static InquiryEvent ToInquiryEvent(EventResult e) => new(
		Id: e.Id,
		Stream: e.Stream,
		EventNumber: e.EventNumber,
		EventType: e.EventType,
		Timestamp: e.Timestamp,
		Score: e.Score.HasValue ? MathF.Round(e.Score.Value, 4) : null,
		Redacted: e.AccessDenied,
		Data: e.Data,
		Metadata: e.Metadata);
}

public sealed class InquiryNotFoundException(string inquiryId)
	: ClientFacingException($"Inquiry {inquiryId} not found (expired or invalid). Open a new one with inq_new.") {
	public string InquiryId { get; } = inquiryId;
}
