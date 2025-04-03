// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Threading.Tasks;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.SystemData;
using EventStore.ClientAPI.UserManagement;
using NUnit.Framework;
using HttpStatusCode = EventStore.ClientAPI.Transport.Http.HttpStatusCode;

namespace EventStore.Core.Tests.ClientAPI.UserManagement;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class updating_a_user<TLogFormat, TStreamId>  : TestWithNode<TLogFormat, TStreamId>  {
	[Test]
	public async Task updating_a_user_with_null_username_throws() {
		await AssertEx.ThrowsAsync<ArgumentNullException>(() =>
			_manager.UpdateUserAsync(null, "greg", new[] { "foo", "bar" }, new UserCredentials("admin", "changeit")));
	}

	[Test]
	public async Task updating_a_user_with_empty_username_throws() {
		await AssertEx.ThrowsAsync<ArgumentNullException>(() =>
			_manager.UpdateUserAsync("", "greg", new[] { "foo", "bar" }, new UserCredentials("admin", "changeit")));
	}

	[Test]
	public async Task updating_a_user_with_null_name_throws() {
		await AssertEx.ThrowsAsync<ArgumentNullException>(() =>
			_manager.UpdateUserAsync("greg", null, new[] { "foo", "bar" }, new UserCredentials("admin", "changeit")));
	}

	[Test]
	public async Task updating_a_user_with_empty_name_throws() {
		await AssertEx.ThrowsAsync<ArgumentNullException>(() =>
			_manager.UpdateUserAsync("greg", "", new[] { "foo", "bar" }, new UserCredentials("admin", "changeit")));
	}

	[Test]
	public async Task updating_non_existing_user_throws() {
		await AssertEx.ThrowsAsync<UserCommandFailedException>(() => _manager.UpdateUserAsync(Guid.NewGuid().ToString(), "bar",
			new[] { "foo" }, new UserCredentials("admin", "changeit")));
	}

	[Test]
	public async Task updating_a_user_with_parameters_can_be_read() {
		UserDetails d = null;
		await _manager.CreateUserAsync("ouro", "ourofull", new[] { "foo", "bar" }, "password",
			new UserCredentials("admin", "changeit"));
		await _manager.UpdateUserAsync("ouro", "something", new[] { "bar", "baz" },
			new UserCredentials("admin", "changeit"));
		d = await _manager.GetUserAsync("ouro", new UserCredentials("admin", "changeit"));
		Assert.AreEqual("ouro", d.LoginName);
		Assert.AreEqual("something", d.FullName);
		Assert.AreEqual("bar", d.Groups[0]);
		Assert.AreEqual("baz", d.Groups[1]);
	}
}
