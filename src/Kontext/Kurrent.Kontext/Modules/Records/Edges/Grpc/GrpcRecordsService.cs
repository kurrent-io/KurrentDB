// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace Kurrent.Kontext.Records.Grpc;

/// <summary>
/// The gRPC edge for the records service — a thin transport shim over <see cref="IKontextRecords"/>.
/// It owns only the gRPC plumbing (<see cref="ServerCallContext"/>); all request shaping, domain mapping,
/// and validation live behind the service and its decorators.
/// </summary>
public sealed class GrpcRecordsService(IKontextRecords service) : Contracts.RecordsService.RecordsServiceBase {
	public override async Task<Contracts.SearchResponse> Search(Contracts.SearchRequest request, ServerCallContext context) =>
		await service.SearchAsync(request, context.CancellationToken).ConfigureAwait(false);

	public override async Task<Contracts.QueryResponse> Query(Contracts.QueryRequest request, ServerCallContext context) =>
		await service.QueryAsync(request, context.GetHttpContext().User, context.CancellationToken).ConfigureAwait(false);
}
