// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS8524

// ReSharper disable CheckNamespace

using System.Security.Claims;
using EventStore.Plugins.Authorization;
using KurrentDB.Api.Errors;
using static EventStore.Plugins.Authorization.Operations.Streams.Parameters;

namespace KurrentDB.Api.Streams.Authorization;

public static class AuthorizationExtensions {
	/// <summary>
	/// Authorizes a user for a specific operation on a stream.
	/// If the user is not authorized, an <see cref="Grpc.Core.RpcException"/> is thrown.
	/// </summary>
	public static async Task AuthorizeStreamOperation(this IAuthorizationProvider authz, StreamName stream, StreamOperation operation, ClaimsPrincipal user, CancellationToken ct) {
		Operation op = operation switch {
			StreamOperation.Read          => Operations.Streams.Read,
			StreamOperation.Write         => Operations.Streams.Write,
			StreamOperation.Delete        => Operations.Streams.Delete,
			StreamOperation.MetadataRead  => Operations.Streams.MetadataRead,
			StreamOperation.MetadataWrite => Operations.Streams.MetadataWrite,
		};

		var accessGranted = await authz
			.CheckAccessAsync(user, op.WithParameter(StreamId(stream)), ct);

		if (!accessGranted)
			throw ApiErrors.AccessDenied($"{nameof(Operations.Streams)}:{operation}".ToLowerInvariant(), user.Identity?.Name);
	}

    public static Task AuthorizeStreamRead(this IAuthorizationProvider authz, StreamName stream, ClaimsPrincipal user, CancellationToken ct) =>
        authz.AuthorizeStreamOperation(stream, StreamOperation.Read, user, ct);

    public static Task AuthorizeStreamWrite(this IAuthorizationProvider authz, StreamName stream, ClaimsPrincipal user, CancellationToken ct) =>
        authz.AuthorizeStreamOperation(stream, StreamOperation.Write, user, ct);

    public static Task AuthorizeStreamDelete(this IAuthorizationProvider authz, StreamName stream, ClaimsPrincipal user, CancellationToken ct) =>
        authz.AuthorizeStreamOperation(stream, StreamOperation.Delete, user, ct);

    public static Task AuthorizeStreamMetadataRead(this IAuthorizationProvider authz, StreamName stream, ClaimsPrincipal user, CancellationToken ct) =>
        authz.AuthorizeStreamOperation(stream, StreamOperation.MetadataRead, user, ct);

    public static Task AuthorizeStreamMetadataWrite(this IAuthorizationProvider authz, StreamName stream, ClaimsPrincipal user, CancellationToken ct) =>
        authz.AuthorizeStreamOperation(stream, StreamOperation.MetadataWrite, user, ct);
}
