// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Streams;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using Xunit;

namespace KurrentDB.Components.Tests;

// /ui/streams is the real authorization gap (plain [Authorize], no core op-level check). These pin that
// every StreamsService op authorizes with the same Operation + StreamId the gRPC/HTTP read/append path
// uses, before publishing. $all reads authorize against "$all"; metastream ($$x) read/write are the
// metadata cases (the engine translates them to metadataRead/metadataWrite on the base stream).
public class StreamsServiceAuthorizationTests {
	static (StreamsService Service, RecordingAuthorizationProvider Authz, List<Message> Published) NewService() {
		var published = new List<Message>();
		var authz = new RecordingAuthorizationProvider(grant: false);
		var service = new StreamsService(new ReplyPublisher(published.Add), authz);
		return (service, authz, published);
	}

	static readonly ClaimsPrincipal SomeUser = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test"));

	// Two overloads because ValueTask and ValueTask<T> share no base type; both adapt to AssertDenied.
	Task AssertRequiresAccessTo<T>(Operation expected, Func<StreamsService, ValueTask<T>> op) =>
		AssertDenied(expected, s => op(s).AsTask());

	Task AssertRequiresAccessTo(Operation expected, Func<StreamsService, ValueTask> op) =>
		AssertDenied(expected, s => op(s).AsTask());

	Task AssertRequiresAccessTo(Operation expected, Func<StreamsService, Task> op) =>
		AssertDenied(expected, op);

	async Task AssertDenied(Operation expected, Func<StreamsService, Task> op) {
		var (service, authz, published) = NewService();

		await Assert.ThrowsAsync<StreamsException>(() => op(service));

		Assert.Empty(published);                       // denial short-circuited before publishing
		Assert.NotNull(authz.LastOperation);
		var actual = authz.LastOperation!.Value;
		Assert.Equal((OperationDefinition)expected, (OperationDefinition)actual);   // resource + action
		Assert.Equal(expected.Parameters.ToArray(), actual.Parameters.ToArray());   // the StreamId
	}

	static Operation Read(string streamId) =>
		new Operation(Operations.Streams.Read).WithParameter(Operations.Streams.Parameters.StreamId(streamId));
	static Operation Write(string streamId) =>
		new Operation(Operations.Streams.Write).WithParameter(Operations.Streams.Parameters.StreamId(streamId));
	static Operation Delete(string streamId) =>
		new Operation(Operations.Streams.Delete).WithParameter(Operations.Streams.Parameters.StreamId(streamId));

	[Fact]
	public Task ReadStreamForward_requires_read() =>
		AssertRequiresAccessTo(Read("orders"), s => s.ReadStreamForwardAsync(SomeUser, "orders", 0, 10, CancellationToken.None));

	[Fact]
	public Task ReadStreamBackward_requires_read() =>
		AssertRequiresAccessTo(Read("orders"), s => s.ReadStreamBackwardAsync(SomeUser, "orders", -1, 10, CancellationToken.None));

	[Fact]
	public Task ReadAllBackward_requires_read_of_all() =>
		AssertRequiresAccessTo(Read("$all"), s => s.ReadAllBackwardAsync(SomeUser, -1, -1, 10, CancellationToken.None));

	[Fact]
	public Task ReadAllForward_requires_read_of_all() =>
		AssertRequiresAccessTo(Read("$all"), s => s.ReadAllForwardAsync(SomeUser, 0, 0, 10, CancellationToken.None));

	[Fact]
	public Task ReadEvent_requires_read() =>
		AssertRequiresAccessTo(Read("orders"), s => s.ReadEventAsync(SomeUser, "orders", 0, CancellationToken.None));

	[Fact]
	public Task Write_requires_write() =>
		AssertRequiresAccessTo(Write("orders"), s => s.WriteEventAsync(SomeUser, "orders", "evt", "{}", "{}", CancellationToken.None));

	[Fact]
	public Task Delete_requires_delete() =>
		AssertRequiresAccessTo(Delete("orders"), s => s.DeleteStreamAsync(SomeUser, "orders", CancellationToken.None));

	[Fact]
	public Task GetMetadata_requires_read_of_the_metastream() =>
		AssertRequiresAccessTo(Read("$$orders"), s => s.GetMetadataAsync(SomeUser, "orders", CancellationToken.None));

	[Fact]
	public Task UpdateAcl_requires_write_of_the_metastream() =>
		// acl is only used after the (denied) authorization check, so a null is never dereferenced here.
		AssertRequiresAccessTo(Write("$$orders"), s => s.UpdateAclAsync(SomeUser, "orders", null!, CancellationToken.None));

	[Fact]
	public Task Subscribe_requires_read() =>
		AssertRequiresAccessTo(Read("orders"), s => s.SubscribeAsync(SomeUser, "orders", _ => { }, resolveLinkTos: true, CancellationToken.None));

	[Fact]
	public Task SubscribeAll_requires_read_of_all() =>
		AssertRequiresAccessTo(Read("$all"), s => s.SubscribeAsync(SomeUser, "$all", _ => { }, resolveLinkTos: true, CancellationToken.None));
}
