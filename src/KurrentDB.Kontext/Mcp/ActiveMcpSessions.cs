// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Security.Claims;

namespace KurrentDB.Kontext.Mcp;

public class ActiveMcpSessions {
	readonly record struct Session(string BaseUrl, string Workspace, ClaimsPrincipal Principal);

	readonly ConcurrentDictionary<string, Session> _sessions = new();

	public bool Add(string sessionId, string baseUrl, string workspace, ClaimsPrincipal principal) =>
		_sessions.TryAdd(sessionId, new Session(baseUrl, workspace, principal));

	public bool Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

	public bool Contains(string sessionId) => _sessions.ContainsKey(sessionId);

	public bool IsBoundTo(string sessionId, string workspace) =>
		_sessions.TryGetValue(sessionId, out var session) && session.Workspace == workspace;

	public string? GetBaseUrl(string sessionId) =>
		_sessions.TryGetValue(sessionId, out var session) ? session.BaseUrl : null;

	public ClaimsPrincipal? GetPrincipal(string sessionId) =>
		_sessions.TryGetValue(sessionId, out var session) ? session.Principal : null;
}
