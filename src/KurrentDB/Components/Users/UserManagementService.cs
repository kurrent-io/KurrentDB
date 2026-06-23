// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Components.Users;

public class UserManagementService(IPublisher publisher, IAuthorizationProvider authorizer) {
	// Same operations the gRPC Users service / HTTP UsersController authorize against. The UI publishes
	// core messages directly, bypassing the transport layer, so it must perform the equivalent check here.
	static readonly Operation ListOp = new(Operations.Users.List);
	static readonly Operation ReadOp = new(Operations.Users.Read);
	static readonly Operation CreateOp = new(Operations.Users.Create);
	static readonly Operation UpdateOp = new(Operations.Users.Update);
	static readonly Operation DeleteOp = new(Operations.Users.Delete);
	static readonly Operation EnableOp = new(Operations.Users.Enable);
	static readonly Operation DisableOp = new(Operations.Users.Disable);
	static readonly Operation ResetPasswordOp = new(Operations.Users.ResetPassword);

	public async ValueTask<UserManagementMessage.UserData[]> GetAllAsync(ClaimsPrincipal principal, CancellationToken ct) {
		await Authorize(principal, ListOp, ct);
		var envelope = new TcsEnvelope<UserManagementMessage.AllUserDetailsResult>();
		publisher.Publish(new UserManagementMessage.GetAll(envelope, principal));
		var result = await envelope.Task.WaitAsync(ct);
		return result.Success ? result.Data : throw new UserManagementException(result.Error);
	}

	public async ValueTask<UserManagementMessage.UserData> GetAsync(ClaimsPrincipal principal, string loginName, CancellationToken ct) {
		await Authorize(principal, ReadOp, ct);
		var envelope = new TcsEnvelope<UserManagementMessage.UserDetailsResult>();
		publisher.Publish(new UserManagementMessage.Get(envelope, principal, loginName));
		var result = await envelope.Task.WaitAsync(ct);
		return result.Success ? result.Data : throw new UserManagementException(result.Error);
	}

	public async ValueTask CreateAsync(ClaimsPrincipal principal, string loginName, string fullName, string[] groups, string password, CancellationToken ct) {
		await Authorize(principal, CreateOp, ct);
		var envelope = new TcsEnvelope<UserManagementMessage.UpdateResult>();
		publisher.Publish(new UserManagementMessage.Create(envelope, principal, loginName, fullName, groups, password));
		CheckResult(await envelope.Task.WaitAsync(ct));
	}

	public async ValueTask UpdateAsync(ClaimsPrincipal principal, string loginName, string fullName, string[] groups, CancellationToken ct) {
		await Authorize(principal, UpdateOp, ct);
		var envelope = new TcsEnvelope<UserManagementMessage.UpdateResult>();
		publisher.Publish(new UserManagementMessage.Update(envelope, principal, loginName, fullName, groups));
		CheckResult(await envelope.Task.WaitAsync(ct));
	}

	public async ValueTask DeleteAsync(ClaimsPrincipal principal, string loginName, CancellationToken ct) {
		await Authorize(principal, DeleteOp, ct);
		var envelope = new TcsEnvelope<UserManagementMessage.UpdateResult>();
		publisher.Publish(new UserManagementMessage.Delete(envelope, principal, loginName));
		CheckResult(await envelope.Task.WaitAsync(ct));
	}

	public async ValueTask EnableAsync(ClaimsPrincipal principal, string loginName, CancellationToken ct) {
		await Authorize(principal, EnableOp, ct);
		var envelope = new TcsEnvelope<UserManagementMessage.UpdateResult>();
		publisher.Publish(new UserManagementMessage.Enable(envelope, principal, loginName));
		CheckResult(await envelope.Task.WaitAsync(ct));
	}

	public async ValueTask DisableAsync(ClaimsPrincipal principal, string loginName, CancellationToken ct) {
		await Authorize(principal, DisableOp, ct);
		var envelope = new TcsEnvelope<UserManagementMessage.UpdateResult>();
		publisher.Publish(new UserManagementMessage.Disable(envelope, principal, loginName));
		CheckResult(await envelope.Task.WaitAsync(ct));
	}

	public async ValueTask ResetPasswordAsync(ClaimsPrincipal principal, string loginName, string newPassword, CancellationToken ct) {
		await Authorize(principal, ResetPasswordOp, ct);
		var envelope = new TcsEnvelope<UserManagementMessage.UpdateResult>();
		publisher.Publish(new UserManagementMessage.ResetPassword(envelope, principal, loginName, newPassword));
		CheckResult(await envelope.Task.WaitAsync(ct));
	}

	// Enforce the configured authorization policy before publishing, translating a denial into the
	// domain exception the components already handle.
	async ValueTask Authorize(ClaimsPrincipal principal, Operation operation, CancellationToken ct) {
		if (!await authorizer.CheckAccessAsync(principal, operation, ct))
			throw new UserManagementException(UserManagementMessage.Error.Unauthorized);
	}

	static void CheckResult(UserManagementMessage.UpdateResult result) {
		if (!result.Success)
			throw new UserManagementException(result.Error);
	}
}

public class UserManagementException : Exception {
	public UserManagementMessage.Error Error { get; }

	public UserManagementException(UserManagementMessage.Error error)
		: base(error switch {
			UserManagementMessage.Error.NotFound => "User not found.",
			UserManagementMessage.Error.Conflict => "A user with this login name already exists.",
			UserManagementMessage.Error.Unauthorized => "Access denied.",
			UserManagementMessage.Error.TryAgain => "The server is busy. Please try again.",
			_ => "An unexpected error occurred."
		}) {
		Error = error;
	}
}
