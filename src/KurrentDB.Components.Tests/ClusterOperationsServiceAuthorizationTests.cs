// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Cluster;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Core.Messaging;
using Xunit;

namespace KurrentDB.Components.Tests;

// Node operations authorize against Operations.Node.* (OperationsOrAdmins by default), the same way the
// gRPC Operations service does, before publishing the shutdown/resign message. Recording-deny proves the
// UI does not bypass the policy and that it asks for the right operation.
public class ClusterOperationsServiceAuthorizationTests {
	static (ClusterOperationsService Service, RecordingAuthorizationProvider Authz, List<Message> Published) NewService() {
		var published = new List<Message>();
		var authz = new RecordingAuthorizationProvider(grant: false);
		var service = new ClusterOperationsService(new ReplyPublisher(published.Add), authz);
		return (service, authz, published);
	}

	static readonly ClaimsPrincipal SomeUser = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test"));

	async Task AssertRequiresAccessTo(OperationDefinition expected, Func<ClusterOperationsService, ValueTask> op) {
		var (service, authz, published) = NewService();

		await Assert.ThrowsAsync<OperationsAccessDeniedException>(() => op(service).AsTask());

		Assert.Empty(published);                       // denial short-circuited before publishing
		Assert.NotNull(authz.LastOperation);
		Assert.Equal(expected, (OperationDefinition)authz.LastOperation!.Value);
	}

	[Fact]
	public Task Shutdown_requires_node_shutdown() =>
		AssertRequiresAccessTo(Operations.Node.Shutdown, s => s.ShutdownAsync(SomeUser, CancellationToken.None));

	[Fact]
	public Task Resign_requires_node_resign() =>
		AssertRequiresAccessTo(Operations.Node.Resign, s => s.ResignNodeAsync(SomeUser, CancellationToken.None));
}
