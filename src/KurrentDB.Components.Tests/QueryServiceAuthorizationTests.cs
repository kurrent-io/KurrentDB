// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Query;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Core.Services;
using KurrentDB.SecondaryIndexing.Query;
using Xunit;

namespace KurrentDB.Components.Tests;

// Ad-hoc SQL runs against the secondary-index query engine, which can read across all streams. The FlightSql
// transport authorizes every query against Streams.Read on $all; the UI does the same before executing.
// Recording-deny proves the authorization happens (and short-circuits) before the engine is touched — so the
// query engine is never reached on denial (a null engine receiver is fine here).
public class QueryServiceAuthorizationTests {
	static readonly ClaimsPrincipal SomeUser = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test"));

	[Fact]
	public async Task ExecuteAdHocUserQuery_requires_read_on_all() {
		var authz = new RecordingAuthorizationProvider(grant: false);

		await Assert.ThrowsAsync<AccessDeniedException>(
			() => ((IQueryEngine)null!).ExecuteAdHocUserQuery(authz, SomeUser, "SELECT 1", CancellationToken.None).AsTask());

		Assert.NotNull(authz.LastOperation);
		var actual = authz.LastOperation!.Value;
		var expected = new Operation(Operations.Streams.Read)
			.WithParameter(Operations.Streams.Parameters.StreamId(SystemStreams.AllStream));
		Assert.Equal((OperationDefinition)expected, (OperationDefinition)actual);     // resource + action
		Assert.Equal(expected.Parameters.ToArray(), actual.Parameters.ToArray());     // the $all StreamId
	}
}
