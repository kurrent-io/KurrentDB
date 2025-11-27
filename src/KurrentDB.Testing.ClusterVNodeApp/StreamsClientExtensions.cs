// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Protocol.V2.Streams;
using StreamsService = KurrentDB.Protocol.V2.Streams.StreamsService;

namespace KurrentDB.Testing;

public static class StreamsClientExtensions {
	public static async ValueTask<AppendResponse> AppendAsync(this StreamsService.StreamsServiceClient client, AppendRequest request, CancellationToken cancellationToken) {
		using var session = client.AppendSession(cancellationToken: cancellationToken);
		await session.RequestStream.WriteAsync(request, cancellationToken);
		await session.RequestStream.CompleteAsync();
		var response = await session.ResponseAsync;
		return response.Output[0];
	}

	public static async ValueTask<AppendResponse> AppendAsync(this StreamsService.StreamsServiceClient client, AppendRequest request, CallOptions callOptions) {
		using var session = client.AppendSession(cancellationToken: callOptions.CancellationToken);
		await session.RequestStream.WriteAsync(request, callOptions.CancellationToken);
		await session.RequestStream.CompleteAsync();
		var response = await session.ResponseAsync;
		return response.Output[0];
	}
}
