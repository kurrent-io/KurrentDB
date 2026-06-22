// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Mcp;

/// <summary>
/// Marker base class for exceptions whose message is safe and useful to surface to an MCP caller
/// (agent) as a tool error. The call-tool filter relies on this — types not deriving from it fall
/// through to the SDK's generic "An error occurred invoking '<tool>'." so internal details aren't
/// leaked.
/// </summary>
public abstract class ClientFacingException(string message, Exception? inner = null)
	: Exception(message, inner);

/// <summary>
/// Thrown by MCP tools for caller-actionable input validation (e.g. count out of range, fact text
/// empty). Prefer this over <see cref="ArgumentException"/> for tool input so internal argument
/// errors don't accidentally reach the caller.
/// </summary>
public sealed class ToolInputException(string message) : ClientFacingException(message);
