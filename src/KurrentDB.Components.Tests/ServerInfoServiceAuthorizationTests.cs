// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.ServerInfo;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Core.Messaging;
using Xunit;

namespace KurrentDB.Components.Tests;

// $server-info is read/written as a normal stream; the service authorizes Streams.Read/Write with the
// $server-info StreamId — the same ops the gRPC/HTTP read+append paths use — before publishing. Recording-deny
// proves no-bypass and the right operation + StreamId. (UpdateAsync authorizes the Write first, so a denial
// short-circuits before the metadata write/read.)
public class ServerInfoServiceAuthorizationTests {
	static (ServerInfoService Service, RecordingAuthorizationProvider Authz, List<Message> Published) NewService() {
		var published = new List<Message>();
		var authz = new RecordingAuthorizationProvider(grant: false);
		var service = new ServerInfoService(new ReplyPublisher(published.Add), authz);
		return (service, authz, published);
	}

	static readonly ClaimsPrincipal SomeUser = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test"));

	async Task AssertRequiresAccessTo(Operation expected, Func<ServerInfoService, Task> op) {
		var (service, authz, published) = NewService();

		await Assert.ThrowsAsync<ServerInfoException>(() => op(service));

		Assert.Empty(published);                       // denial short-circuited before publishing
		Assert.NotNull(authz.LastOperation);
		var actual = authz.LastOperation!.Value;
		Assert.Equal((OperationDefinition)expected, (OperationDefinition)actual);    // resource + action
		Assert.Equal(expected.Parameters.ToArray(), actual.Parameters.ToArray());    // the $server-info StreamId
	}

	static Operation Read(string streamId) =>
		new Operation(Operations.Streams.Read).WithParameter(Operations.Streams.Parameters.StreamId(streamId));
	static Operation Write(string streamId) =>
		new Operation(Operations.Streams.Write).WithParameter(Operations.Streams.Parameters.StreamId(streamId));

	[Fact]
	public Task Get_requires_read_on_server_info() =>
		AssertRequiresAccessTo(Read("$server-info"), s => s.GetAsync(SomeUser, CancellationToken.None).AsTask());

	[Fact]
	public Task Update_requires_write_on_server_info() =>
		AssertRequiresAccessTo(Write("$server-info"),
			s => s.UpdateAsync(SomeUser, new ServerInfoData { Name = "x" }, CancellationToken.None).AsTask());
}
