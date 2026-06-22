// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Scavenges;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Core.Messaging;
using Xunit;

namespace KurrentDB.Components.Tests;

// Scavenge ops must enforce authorization like the gRPC Operations.Scavenge service / streams read path:
// start/stop against Node.Scavenge.*, and history/detail as Streams.Read of the $scavenges streams (with
// the stream-id parameter). The recording-deny provider proves no-bypass + fidelity (incl. parameters).
public class ScavengeServiceAuthorizationTests {
	static (ScavengeService Service, RecordingAuthorizationProvider Authz, List<Message> Published) NewService() {
		var published = new List<Message>();
		var authz = new RecordingAuthorizationProvider(grant: false);
		var service = new ScavengeService(new ReplyPublisher(published.Add), authz);
		return (service, authz, published);
	}

	static readonly ClaimsPrincipal SomeUser = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test"));

	// Asserts the call requires access to `expected`: a denial throws and publishes nothing (no-bypass),
	// and the authorized operation matches `expected` in resource+action AND parameters (fidelity).
	// Two overloads because ValueTask and ValueTask<T> share no base type; both adapt to AssertDenied.
	Task AssertRequiresAccessTo<T>(Operation expected, Func<ScavengeService, ValueTask<T>> op) =>
		AssertDenied(expected, s => op(s).AsTask());

	Task AssertRequiresAccessTo(Operation expected, Func<ScavengeService, ValueTask> op) =>
		AssertDenied(expected, s => op(s).AsTask());

	async Task AssertDenied(Operation expected, Func<ScavengeService, Task> op) {
		var (service, authz, published) = NewService();

		await Assert.ThrowsAsync<ScavengeException>(() => op(service));

		Assert.Empty(published);                       // denial short-circuited before publishing
		Assert.NotNull(authz.LastOperation);
		var actual = authz.LastOperation!.Value;
		Assert.Equal((OperationDefinition)expected, (OperationDefinition)actual);   // resource + action
		Assert.Equal(expected.Parameters.ToArray(), actual.Parameters.ToArray());   // e.g. the StreamId
	}

	static Operation ReadStream(string streamId) =>
		new Operation(Operations.Streams.Read).WithParameter(Operations.Streams.Parameters.StreamId(streamId));

	[Fact]
	public Task Start_requires_node_scavenge_start() =>
		AssertRequiresAccessTo(new Operation(Operations.Node.Scavenge.Start),
			s => s.StartAsync(SomeUser, CancellationToken.None));

	[Fact]
	public Task Stop_requires_node_scavenge_stop() =>
		AssertRequiresAccessTo(new Operation(Operations.Node.Scavenge.Stop),
			s => s.StopAsync(SomeUser, "scavenge-1", CancellationToken.None));

	[Fact]
	public Task History_requires_read_of_the_scavenges_stream() =>
		AssertRequiresAccessTo(ReadStream("$scavenges"),
			s => s.GetHistoryAsync(SomeUser, CancellationToken.None));

	[Fact]
	public Task Detail_requires_read_of_the_per_scavenge_stream() =>
		AssertRequiresAccessTo(ReadStream("$scavenges-abc"),
			s => s.GetDetailPageAsync(SomeUser, "abc", -1, 20, CancellationToken.None));
}
