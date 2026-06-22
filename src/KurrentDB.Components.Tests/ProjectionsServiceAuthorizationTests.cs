// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Projections;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Services;
using Xunit;

namespace KurrentDB.Components.Tests;

// Projection ops authorize against the same Operations.Projections.* the configured policy uses (claim-based,
// no resource parameter). Notably the stats/state reads carry no RunAs in the core, so this is their access
// gate. Recording-deny proves no-bypass + the right operation.
public class ProjectionsServiceAuthorizationTests {
	static (ProjectionsService Service, RecordingAuthorizationProvider Authz, List<Message> Published) NewService() {
		var published = new List<Message>();
		var authz = new RecordingAuthorizationProvider(grant: false);
		var service = new ProjectionsService(new ReplyPublisher(published.Add), authz);
		return (service, authz, published);
	}

	static readonly ClaimsPrincipal SomeUser = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test"));

	Task AssertRequiresAccessTo<T>(OperationDefinition expected, Func<ProjectionsService, ValueTask<T>> op) =>
		AssertDenied(expected, s => op(s).AsTask());

	Task AssertRequiresAccessTo(OperationDefinition expected, Func<ProjectionsService, ValueTask> op) =>
		AssertDenied(expected, s => op(s).AsTask());

	async Task AssertDenied(OperationDefinition expected, Func<ProjectionsService, Task> op) {
		var (service, authz, published) = NewService();

		await Assert.ThrowsAsync<ProjectionsException>(() => op(service));

		Assert.Empty(published);                       // denial short-circuited before publishing
		Assert.NotNull(authz.LastOperation);
		Assert.Equal(expected, (OperationDefinition)authz.LastOperation!.Value);
	}

	[Fact]
	public Task GetAll_requires_statistics() =>
		AssertRequiresAccessTo(Operations.Projections.Statistics, s => s.GetAllAsync(SomeUser, CancellationToken.None));

	[Fact]
	public Task GetStatistics_requires_statistics() =>
		AssertRequiresAccessTo(Operations.Projections.Statistics, s => s.GetStatisticsAsync(SomeUser, "p", CancellationToken.None));

	[Fact]
	public Task GetQuery_requires_read() =>
		AssertRequiresAccessTo(Operations.Projections.Read, s => s.GetQueryAsync(SomeUser, "p", CancellationToken.None));

	[Fact]
	public Task GetState_requires_state() =>
		AssertRequiresAccessTo(Operations.Projections.State, s => s.GetStateAsync(SomeUser, "p", CancellationToken.None));

	[Fact]
	public Task Create_requires_create() =>
		AssertRequiresAccessTo(Operations.Projections.Create,
			s => s.CreateAsync(SomeUser, "p", "fromAll()", ProjectionMode.Continuous, false, false, false, CancellationToken.None));

	[Fact]
	public Task Enable_requires_enable() =>
		AssertRequiresAccessTo(Operations.Projections.Enable, s => s.EnableAsync(SomeUser, "p", CancellationToken.None));

	[Fact]
	public Task Disable_requires_disable() =>
		AssertRequiresAccessTo(Operations.Projections.Disable, s => s.DisableAsync(SomeUser, "p", CancellationToken.None));

	[Fact]
	public Task Reset_requires_reset() =>
		AssertRequiresAccessTo(Operations.Projections.Reset, s => s.ResetAsync(SomeUser, "p", CancellationToken.None));

	[Fact]
	public Task Delete_requires_delete() =>
		AssertRequiresAccessTo(Operations.Projections.Delete, s => s.DeleteAsync(SomeUser, "p", true, true, true, CancellationToken.None));

	[Fact]
	public Task UpdateQuery_requires_update() =>
		AssertRequiresAccessTo(Operations.Projections.Update, s => s.UpdateQueryAsync(SomeUser, "p", "fromAll()", null, CancellationToken.None));

	[Fact]
	public Task GetConfig_requires_read_configuration() =>
		AssertRequiresAccessTo(Operations.Projections.ReadConfiguration, s => s.GetConfigAsync(SomeUser, "p", CancellationToken.None));

	[Fact]
	public Task UpdateConfig_requires_update_configuration() =>
		AssertRequiresAccessTo(Operations.Projections.UpdateConfiguration,
			s => s.UpdateConfigAsync(SomeUser, "p", false, false, 0, 0, 0, 0, 0, 0, null, CancellationToken.None));
}
