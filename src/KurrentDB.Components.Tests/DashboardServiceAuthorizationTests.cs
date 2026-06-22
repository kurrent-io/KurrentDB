// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Dashboard;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Core.Messaging;
using Xunit;

namespace KurrentDB.Components.Tests;

// The dashboard's queue-stats read publishes MonitoringMessage.GetFreshStats, the same path the HTTP /stats
// endpoint uses — which gates on Operations.Node.Statistics.Read. Recording-deny proves the UI authorizes
// before publishing and asks for the right operation. (The polling loop swallows the denial and shows no data.)
public class DashboardServiceAuthorizationTests {
	static readonly ClaimsPrincipal SomeUser = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test"));

	[Fact]
	public async Task GetStats_requires_node_statistics_read() {
		var published = new List<Message>();
		var authz = new RecordingAuthorizationProvider(grant: false);
		var service = new DashboardService(new ReplyPublisher(published.Add), authz);

		await Assert.ThrowsAsync<AccessDeniedException>(
			() => service.GetStatsAsync(SomeUser, CancellationToken.None).AsTask());

		Assert.Empty(published);                       // denial short-circuited before publishing
		Assert.NotNull(authz.LastOperation);
		Assert.Equal((OperationDefinition)Operations.Node.Statistics.Read, (OperationDefinition)authz.LastOperation!.Value);
	}
}
