// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.PersistentSubscriptions;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using Xunit;

namespace KurrentDB.Components.Tests;

// Persistent-subscription ops authorize against Operations.Subscriptions.* (claim-based). The stats reads
// (list/detail) and viewing parked messages all authorize as Statistics — their access gate, since those
// paths carry no principal-based check in the core. Recording-deny proves no-bypass + the right operation.
public class PersistentSubscriptionsServiceAuthorizationTests {
	static (PersistentSubscriptionsService Service, RecordingAuthorizationProvider Authz, List<Message> Published) NewService() {
		var published = new List<Message>();
		var authz = new RecordingAuthorizationProvider(grant: false);
		var service = new PersistentSubscriptionsService(new ReplyPublisher(published.Add), authz);
		return (service, authz, published);
	}

	static readonly ClaimsPrincipal SomeUser = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test"));

	Task AssertRequiresAccessTo<T>(OperationDefinition expected, Func<PersistentSubscriptionsService, ValueTask<T>> op) =>
		AssertDenied(expected, s => op(s).AsTask());

	Task AssertRequiresAccessTo(OperationDefinition expected, Func<PersistentSubscriptionsService, ValueTask> op) =>
		AssertDenied(expected, s => op(s).AsTask());

	async Task AssertDenied(OperationDefinition expected, Func<PersistentSubscriptionsService, Task> op) {
		var (service, authz, published) = NewService();

		await Assert.ThrowsAsync<PersistentSubscriptionsException>(() => op(service));

		Assert.Empty(published);                       // denial short-circuited before publishing
		Assert.NotNull(authz.LastOperation);
		Assert.Equal(expected, (OperationDefinition)authz.LastOperation!.Value);
	}

	[Fact]
	public Task GetPage_requires_statistics() =>
		AssertRequiresAccessTo(Operations.Subscriptions.Statistics, s => s.GetPageAsync(SomeUser, 0, 50, CancellationToken.None));

	[Fact]
	public Task GetDetail_requires_statistics() =>
		AssertRequiresAccessTo(Operations.Subscriptions.Statistics, s => s.GetDetailAsync(SomeUser, "stream", "group", CancellationToken.None));

	[Fact]
	public Task GetParkedMessages_requires_statistics() =>
		AssertRequiresAccessTo(Operations.Subscriptions.Statistics, s => s.GetParkedMessagesAsync(SomeUser, "stream", "group", -1, 20, CancellationToken.None));

	[Fact]
	public Task Create_requires_create() =>
		AssertRequiresAccessTo(Operations.Subscriptions.Create, s => s.CreateAsync(SomeUser, "stream", "group", new SubscriptionConfig(), CancellationToken.None));

	[Fact]
	public Task CreateToAll_requires_create() =>
		AssertRequiresAccessTo(Operations.Subscriptions.Create, s => s.CreateToAllAsync(SomeUser, "group", new SubscriptionConfig(), CancellationToken.None));

	[Fact]
	public Task Update_requires_update() =>
		AssertRequiresAccessTo(Operations.Subscriptions.Update, s => s.UpdateAsync(SomeUser, "stream", "group", new SubscriptionConfig(), CancellationToken.None));

	[Fact]
	public Task Delete_requires_delete() =>
		AssertRequiresAccessTo(Operations.Subscriptions.Delete, s => s.DeleteAsync(SomeUser, "stream", "group", CancellationToken.None));

	[Fact]
	public Task ReplayParked_requires_replay() =>
		AssertRequiresAccessTo(Operations.Subscriptions.ReplayParked, s => s.ReplayParkedAsync(SomeUser, "stream", "group", CancellationToken.None));
}
