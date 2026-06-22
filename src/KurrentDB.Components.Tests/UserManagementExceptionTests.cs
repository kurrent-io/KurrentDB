// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Components.Users;
using KurrentDB.Core.Messages;
using Xunit;

namespace KurrentDB.Components.Tests;

// Pure unit test of the engine-error -> user-facing-message mapping. No renderer or DI needed.
public class UserManagementExceptionTests {
	[Theory]
	[InlineData(UserManagementMessage.Error.NotFound, "User not found.")]
	[InlineData(UserManagementMessage.Error.Conflict, "A user with this login name already exists.")]
	[InlineData(UserManagementMessage.Error.Unauthorized, "Access denied.")]
	[InlineData(UserManagementMessage.Error.TryAgain, "The server is busy. Please try again.")]
	public void Maps_known_errors_to_friendly_messages(UserManagementMessage.Error error, string expected) {
		var ex = new UserManagementException(error);

		Assert.Equal(expected, ex.Message);
		Assert.Equal(error, ex.Error);   // original error is preserved for callers that branch on it
	}

	[Theory]
	[InlineData(UserManagementMessage.Error.Error)]
	[InlineData(UserManagementMessage.Error.Success)]
	public void Maps_unspecified_errors_to_a_generic_message(UserManagementMessage.Error error) {
		var ex = new UserManagementException(error);

		Assert.Equal("An unexpected error occurred.", ex.Message);
		Assert.Equal(error, ex.Error);
	}
}
