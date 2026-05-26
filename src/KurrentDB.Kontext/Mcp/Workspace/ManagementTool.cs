// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Api;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;
using ModelContextProtocol.Server;

namespace KurrentDB.Kontext.Mcp.Workspace;

public sealed class ManagementUnavailableException(string reason) : ClientFacingException(reason);

public sealed record ManagementInstructionsResult(
	[property: Description("The workspace this agent is bound to. The agent can only search/read/import within this workspace — it cannot switch to another.")]
	string CurrentWorkspace,
	[property: Description("Whether the current workspace is Started or Stopped.")]
	string CurrentWorkspaceState,
	[property: Description("curl commands for each workspace management action. Run them with any HTTP client.")]
	Dictionary<string, string> Commands,
	[property: Description("Constraints to convey to the user and respect before acting.")]
	IReadOnlyList<string> Notes);

[McpServerToolType]
public class ManagementTool {
	public const string WorkspacesPath = "/kontext/workspaces";

	[McpServerTool(Name = "ws_management", UseStructuredContent = true), Description(
		"Manage workspaces. KurrentDB admin or ops credentials are required.")]
	public static ManagementInstructionsResult Manage(
		ActiveMcpSessions sessions,
		WorkspaceRegistry workspaces,
		WorkspaceContext workspaceContext,
		McpServer? server = null) {
		var sessionId = server?.SessionId
			?? throw new ManagementUnavailableException("Workspace management requires HTTP transport. No session ID available.");

		var baseUrl = sessions.GetBaseUrl(sessionId)
			?? throw new ManagementUnavailableException("Session not registered. Workspace management endpoint is not available.");

		var current = workspaceContext.Current;
		if (!workspaces.TryGet(current, out var entry))
			throw new WorkspaceNotFoundException(current);

		return BuildInstructions(baseUrl, current, started: entry.Status == WorkspaceLifecycle.Started);
	}

	const string Curl = "curl --user admin:changeit";

	internal static ManagementInstructionsResult BuildInstructions(string baseUrl, string currentWorkspace, bool started) {
		var api = baseUrl.TrimEnd('/') + WorkspacesPath;
		var current = currentWorkspace;
		var state = started ? "Started" : "Stopped";

		return new ManagementInstructionsResult(
			CurrentWorkspace: current,
			CurrentWorkspaceState: state,
			Commands: new Dictionary<string, string>(StringComparer.Ordinal) {
				["list"] = $"{Curl} {api}",
				["view"] = $"{Curl} {api}/{current}",
				["start"] = $"{Curl} --request POST {api}/{current}/start",
				["stop"] = $"{Curl} --request POST {api}/{current}/stop",
				["create"] = $"{Curl} --request POST {api} --header \"Content-Type: application/json\" " +
					"--data '{\"name\": \"<new-workspace>\", \"filterRules\": [{\"streamPrefix\": \"<stream-prefix>\", \"filter\": \"<optional-js-filter>\"}]}'",
				["delete"] = $"{Curl} --request DELETE {api}/<workspace>",
			},
			Notes:
			[
				"Workspace management requires KurrentDB admin or ops credentials (admin:changeit by default).",
				"Always ask the user for permission before starting, stopping, creating or deleting a workspace.",
				$"If '{current}' is Stopped, indexing and search are paused; with the user's permission, run the 'start' command to resume it.",
				$"You are bound to the '{current}' workspace. Even if you create or start another workspace, you cannot connect to it — only the user can, by updating this agent's MCP server configuration to point at the other workspace's endpoint.",
				"The start/stop/view commands target the current workspace — replace the name to act on a different one.",
				"Add the --insecure flag to curl to skip TLS verification for local / dev certificates.",
			]);
	}
}
