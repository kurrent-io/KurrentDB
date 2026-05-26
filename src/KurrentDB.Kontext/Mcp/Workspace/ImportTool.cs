// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Api;
using KurrentDB.Kontext.Workspaces.Registry;
using ModelContextProtocol.Server;

namespace KurrentDB.Kontext.Mcp.Workspace;

public class ImportEvent {
	[JsonPropertyName("stream")]
	public string Stream { get; set; } = "";

	[JsonPropertyName("eventType")]
	public string EventType { get; set; } = "";

	[JsonPropertyName("data")]
	public JsonElement Data { get; set; }
}

public sealed class ImportUnavailableException(string reason) : ClientFacingException(reason);

public sealed record AllowedFilterRule(
	[property: Description("Stream-name pattern. The trailing '*' is a wildcard — replace it with your own suffix (e.g. 'order-*' → 'order-123'); '*' alone allows any stream.")]
	string StreamPattern,
	[property: Description("Optional JavaScript filter scoping this workspace to matching events (e.g. rec => rec.value.org === 'acme'); non-matching events are written but not indexed.")]
	string? Filter = null);

public sealed record ImportInstructionsResult(
	[property: Description("HTTP method to use for the bulk-import request.")]
	string Method,
	[property: Description("Full URL of the bulk-import endpoint.")]
	string Url,
	[property: Description("Headers required for the request, including the Mcp-Session-Id binding.")]
	Dictionary<string, string> Headers,
	[property: Description("Stream patterns + optional JS filters that scope this workspace. Imported events must match the stream patterns & filters — otherwise they may be rejected or not indexed.")]
	IReadOnlyList<AllowedFilterRule> AllowedFilterRules,
	[property: Description("Conventions to follow when constructing the payload.")]
	IReadOnlyList<string> Guidelines,
	[property: Description("Description of the JSON shape expected in the request body, with an example.")]
	string PayloadFormat,
	[property: Description("Description of the JSON shape returned in the response.")]
	string ResponseFormat,
	[property: Description("Actions the agent should take after a successful import.")]
	string FollowUp);

[McpServerToolType]
public class ImportTool {
	public const string ImportRouteTemplate = "/kontext/{workspace}/import";

	[McpServerTool(Name = "ws_import", UseStructuredContent = true), Description(
		"Bulk-import endpoint for the current workspace. Build a payload file, then use any HTTP client (curl, wget, Invoke-WebRequest, ...) to POST it.")]
	public static ImportInstructionsResult Import(
		ActiveMcpSessions sessions,
		WorkspaceRegistry workspaces,
		WorkspaceContext workspaceContext,
		McpServer? server = null) {
		var sessionId = server?.SessionId
			?? throw new ImportUnavailableException("Bulk import requires HTTP transport. No session ID available.");

		var baseUrl = sessions.GetBaseUrl(sessionId)
			?? throw new ImportUnavailableException("Session not registered. Bulk import endpoint is not available.");

		if (!workspaces.TryGet(workspaceContext.Current, out var entry))
			throw new WorkspaceNotFoundException(workspaceContext.Current);

		entry.EnsureImportable();

		var endpoint = baseUrl.TrimEnd('/') + ImportRouteTemplate.Replace("{workspace}", workspaceContext.Current);

		return new ImportInstructionsResult(
			Method: "POST",
			Url: endpoint,
			Headers: new Dictionary<string, string>(StringComparer.Ordinal) {
				["Content-Type"] = "application/json",
				["Mcp-Session-Id"] = sessionId,
			},
			AllowedFilterRules: entry.FilterRules.Select(r => new AllowedFilterRule(r.StreamPrefix + "*", r.Filter)).ToArray(),
			Guidelines:
			[
				"Choose stream names that represent the entity or aggregate (e.g. 'customer-123').",
				"Group related events into the same stream so surrounding events can be read together for context.",
				"Choose PascalCase event types that describe what happened (e.g. 'OrderPlaced', 'CustomerRegistered').",
				"Preserve all source fields in the payload — do not discard data.",
				"Imported events are written to real streams, so any other workspace whose filter rules match the same stream prefixes will also index and surface them."
			],
			PayloadFormat:
				"[\n" +
				"  {\"stream\": \"order-123\", \"eventType\": \"OrderPlaced\", \"data\": {\"product\": \"Widget\", \"qty\": 5}},\n" +
				"  {\"stream\": \"order-123\", \"eventType\": \"OrderShipped\", \"data\": {\"carrier\": \"FedEx\"}},\n" +
				"  {\"stream\": \"customer-456\", \"eventType\": \"CustomerRegistered\", \"data\": {\"email\": \"a@b.com\"}},\n" +
				"  {\"stream\": \"other-1\", \"eventType\": \"SomethingHappened\", \"data\": {\"note\": \"rejected — doesn't match allowed prefixes\"}}\n" +
				"]",
			ResponseFormat:
				"{\n" +
				"  \"imported\": 3,\n" +
				"  \"streams\": [\n" +
				"    {\"stream\": \"order-123\", \"events\": 2},\n" +
				"    {\"stream\": \"customer-456\", \"events\": 1}\n" +
				"  ],\n" +
				"  \"errors\": [\n" +
				"    {\"index\": 3, \"message\": \"stream 'other-1' does not match any allowed prefix.\"}\n" +
				"  ]\n" +
				"}\n" +
				"Each error references the 0-based position of the rejected event in the request array. The 'errors' field is omitted when every event was imported successfully.",
			FollowUp:
				"Imported events become searchable via inq_search once indexing has caught up; call ws_status to confirm before searching.\n" +
				"Ask the user: " +
				"\"How deeply should I analyze the imported data to retain facts?\" with the options:\n" +
				"(1) Skip — no analysis.\n" +
				"(2) Summary — high-level overview (entity counts, stream names, event types).\n" +
				"(3) Detailed — patterns, relationships, anomalies, and key insights.\n" +
				"(4) Comprehensive — full analysis with cross-references and domain observations.\n" +
				"Then read the payload file, analyze at the chosen depth, call mem_retain, and delete the payload file.");
	}
}
