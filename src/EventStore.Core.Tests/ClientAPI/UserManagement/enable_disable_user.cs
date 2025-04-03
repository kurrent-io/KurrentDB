// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.SystemData;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.UserManagement;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class enable_disable_user<TLogFormat, TStreamId> : TestWithUser<TLogFormat, TStreamId> {
	[Test]
	public async Task disable_empty_username_throws() {
		await AssertEx.ThrowsAsync<ArgumentNullException>(() =>
			_manager.DisableAsync("", new UserCredentials("admin", "changeit")));
	}

	[Test]
	public async Task disable_null_username_throws() {
		await AssertEx.ThrowsAsync<ArgumentNullException>(() =>
			_manager.DisableAsync(null, new UserCredentials("admin", "changeit")));
	}

	[Test]
	public async Task enable_empty_username_throws() {
		await AssertEx.ThrowsAsync<ArgumentNullException>(() =>
			_manager.EnableAsync("", new UserCredentials("admin", "changeit")));
	}

	[Test]
	public async Task enable_null_username_throws() {
		await AssertEx.ThrowsAsync<ArgumentNullException>(() =>
			_manager.EnableAsync(null, new UserCredentials("admin", "changeit")));
	}

	[Test]
	public async Task can_enable_disable_user() {
		await _manager.DisableAsync(_username, new UserCredentials("admin", "changeit"));

		await AssertEx.ThrowsAsync<UserCommandFailedException>(() =>
			_manager.DisableAsync("foo", new UserCredentials(_username, "password")));

		await _manager.EnableAsync(_username, new UserCredentials("admin", "changeit"));

		var c = await _manager.GetCurrentUserAsync(new UserCredentials(_username, "password"));
	}
}
