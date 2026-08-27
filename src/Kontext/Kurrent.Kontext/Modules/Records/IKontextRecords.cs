// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using RecordsContracts = Kurrent.Kontext.Contracts.Records;

namespace Kurrent.Kontext.Records;

/// <summary>
/// The records service is the edge that lets agents search the indexed log by meaning.
/// The canonical protobuf contracts are used for the wire, but the service itself is agnostic to transport and
/// serialization. The gRPC edge is a thin shim over this service.
/// </summary>
public interface IKontextRecords {
	ValueTask<RecordsContracts.SearchResponse> SearchAsync(RecordsContracts.SearchRequest request, CancellationToken ct = default);

	/// <summary>
	/// Runs a read-only SQL query over the log. The principal is explicit because this is the only
	/// operation that can return any record in the database, and the engine's payload expansion runs
	/// as the system account — the authorization check here is the only one there is.
	/// </summary>
	ValueTask<RecordsContracts.QueryResponse> QueryAsync(RecordsContracts.QueryRequest request, ClaimsPrincipal principal, CancellationToken ct = default);
}
