// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Components.Users;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using Xunit;

namespace KurrentDB.Components.Tests;

// Verifies the UI enforces authorization the same way the gRPC/HTTP front-ends do: every privileged op
// calls IAuthorizationProvider, with the correct Operation, and respects a denial *before* publishing.
// A recording-deny provider proves no-bypass (denial propagates, nothing published) AND fidelity (the
// right Operation was passed) in one shot.
public class UserManagementServiceAuthorizationTests {
	static (UserManagementService Service, RecordingAuthorizationProvider Authz, List<Message> Published) NewService() {
		var published = new List<Message>();
		var authz = new RecordingAuthorizationProvider(grant: false);
		var service = new UserManagementService(new ReplyPublisher(published.Add), authz);
		return (service, authz, published);
	}

	static readonly ClaimsPrincipal SomeUser = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test"));

	// Asserts the call requires access to `expected`: with access denied it must throw Unauthorized and
	// publish nothing (no-bypass), and the authorization check must have been for `expected` (fidelity).
	// Two overloads because ValueTask and ValueTask<T> share no base type; both adapt to AssertDenied.
	Task AssertRequiresAccessTo<T>(OperationDefinition expected, Func<UserManagementService, ValueTask<T>> op) =>
		AssertDenied(expected, s => op(s).AsTask());

	Task AssertRequiresAccessTo(OperationDefinition expected, Func<UserManagementService, ValueTask> op) =>
		AssertDenied(expected, s => op(s).AsTask());

	async Task AssertDenied(OperationDefinition expected, Func<UserManagementService, Task> op) {
		var (service, authz, published) = NewService();

		var ex = await Assert.ThrowsAsync<UserManagementException>(() => op(service));

		Assert.Equal(UserManagementMessage.Error.Unauthorized, ex.Error);
		Assert.Empty(published);                       // denial short-circuited before publishing
		Assert.NotNull(authz.LastOperation);
		OperationDefinition checked_ = authz.LastOperation!.Value;   // implicit Operation -> OperationDefinition
		Assert.Equal(expected, checked_);              // right resource + action
	}

	[Fact]
	public Task GetAll_requires_users_list() =>
		AssertRequiresAccessTo(Operations.Users.List, s => s.GetAllAsync(SomeUser, CancellationToken.None));

	[Fact]
	public Task Get_requires_users_read() =>
		AssertRequiresAccessTo(Operations.Users.Read, s => s.GetAsync(SomeUser, "alice", CancellationToken.None));

	[Fact]
	public Task Create_requires_users_create() =>
		AssertRequiresAccessTo(Operations.Users.Create, s => s.CreateAsync(SomeUser, "alice", "Alice", [], "pw", CancellationToken.None));

	[Fact]
	public Task Update_requires_users_update() =>
		AssertRequiresAccessTo(Operations.Users.Update, s => s.UpdateAsync(SomeUser, "alice", "Alice", [], CancellationToken.None));

	[Fact]
	public Task Delete_requires_users_delete() =>
		AssertRequiresAccessTo(Operations.Users.Delete, s => s.DeleteAsync(SomeUser, "alice", CancellationToken.None));

	[Fact]
	public Task Enable_requires_users_enable() =>
		AssertRequiresAccessTo(Operations.Users.Enable, s => s.EnableAsync(SomeUser, "alice", CancellationToken.None));

	[Fact]
	public Task Disable_requires_users_disable() =>
		AssertRequiresAccessTo(Operations.Users.Disable, s => s.DisableAsync(SomeUser, "alice", CancellationToken.None));

	[Fact]
	public Task ResetPassword_requires_users_reset_password() =>
		AssertRequiresAccessTo(Operations.Users.ResetPassword, s => s.ResetPasswordAsync(SomeUser, "alice", "pw", CancellationToken.None));
}
