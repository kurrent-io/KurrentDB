// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Resources;

namespace Kurrent.Kontext.Mcp;

/// <summary>
/// The agent-facing instructions for the MCP edge — server instructions, tool descriptions, parameter
/// descriptions, and model schema descriptions — sourced from <c>McpInstructions.resx</c>, compiled into
/// the assembly at build time. Key scheme: <c>server.instructions</c>, <c>tool.{name}</c>,
/// <c>tool.{name}.param.{parameter}</c>, <c>type.{Type}</c>, <c>type.{Type}.{jsonProperty}</c>
/// (camelCase property names; nested types join with '+', e.g. <c>type.RecalledMemory+Lean</c>).
/// Lookups return null for missing keys so descriptions are optional by design; only the server
/// instructions are required.
/// </summary>
public static class McpInstructions {
	// MSBuild names the embedded resource after the namespace of the same-named sibling source file
	// (this one), not the folder path — so the base name follows this file's namespace.
	static readonly ResourceManager Resources =
		new("Kurrent.Kontext.Mcp.McpInstructions", typeof(McpInstructions).Assembly);

	public static string Server =>
		Resources.GetString("server.instructions")
		?? throw new InvalidOperationException("McpInstructions.resx is missing the 'server.instructions' entry.");

	public static string? Tool(string tool) =>
		Resources.GetString($"tool.{tool}");

	public static string? Param(string tool, string parameter) =>
		Resources.GetString($"tool.{tool}.param.{parameter}");

	public static string? Type(Type type) =>
		Resources.GetString($"type.{ShortName(type)}");

	public static string? Property(Type declaringType, string jsonName) =>
		Resources.GetString($"type.{ShortName(declaringType)}.{jsonName}");

	static string ShortName(Type type) =>
		type.DeclaringType is null ? type.Name : $"{ShortName(type.DeclaringType)}+{type.Name}";
}
