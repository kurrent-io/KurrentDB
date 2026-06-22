// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Plugins.Authorization;
using KurrentDB.Kontext.Mcp;

namespace KurrentDB.Kontext;

public static class StreamAccessExtensions {
	static readonly Operation ReadOperation = new(Operations.Streams.Read);
	static readonly Operation WriteOperation = new(Operations.Streams.Write);

	public static async ValueTask<bool> CanReadStreamAsync(
		this IAuthorizationProvider authz,
		ClaimsPrincipal user,
		string stream,
		CancellationToken ct = default) {
		var op = ReadOperation.WithParameter(Operations.Streams.Parameters.StreamId(stream));
		return await authz.CheckAccessAsync(user, op, ct);
	}

	public static async ValueTask<bool> CanWriteStreamAsync(
		this IAuthorizationProvider authz,
		ClaimsPrincipal user,
		string stream,
		CancellationToken ct = default) {
		var op = WriteOperation.WithParameter(Operations.Streams.Parameters.StreamId(stream));
		return await authz.CheckAccessAsync(user, op, ct);
	}
}

public sealed class StreamAccessDeniedException(string stream)
	: ClientFacingException($"Read access denied for stream '{stream}'.") {
	public string Stream { get; } = stream;
}