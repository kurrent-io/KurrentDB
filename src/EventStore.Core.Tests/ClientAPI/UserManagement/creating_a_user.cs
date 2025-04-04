// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.ClientAPI.SystemData;
using EventStore.ClientAPI.UserManagement;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.UserManagement;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class creating_a_user<TLogFormat, TStreamId> : TestWithNode<TLogFormat, TStreamId> {
	[Test]
	public void creating_a_user_with_null_username_throws() {
		Assert.Throws<ArgumentNullException>(() =>
			_manager.CreateUserAsync(null, "greg", new[] { "foo", "bar" }, "foofoofoo"));
	}

	[Test]
	public void creating_a_user_with_empty_username_throws() {
		Assert.Throws<ArgumentNullException>(() =>
			_manager.CreateUserAsync("", "ouro", new[] { "foo", "bar" }, "foofoofoo"));
	}

	[Test]
	public void creating_a_user_with_null_name_throws() {
		Assert.Throws<ArgumentNullException>(() =>
			_manager.CreateUserAsync("ouro", null, new[] { "foo", "bar" }, "foofoofoo"));
	}

	[Test]
	public void creating_a_user_with_empty_name_throws() {
		Assert.Throws<ArgumentNullException>(() =>
			_manager.CreateUserAsync("ouro", "", new[] { "foo", "bar" }, "foofoofoo"));
	}


	[Test]
	public void creating_a_user_with_null_password_throws() {
		Assert.Throws<ArgumentNullException>(() =>
			_manager.CreateUserAsync("ouro", "ouro", new[] { "foo", "bar" }, null));
	}

	[Test]
	public void creating_a_user_with_empty_password_throws() {
		Assert.Throws<ArgumentNullException>(() =>
			_manager.CreateUserAsync("ouro", "ouro", new[] { "foo", "bar" }, ""));
	}

	[Test]
	public async System.Threading.Tasks.Task creating_a_user_with_parameters_can_be_readAsync() {
		UserDetails d = null;
		await _manager.CreateUserAsync("ouro", "ourofull", new[] { "foo", "bar" }, "ouro",
			new UserCredentials("admin", "changeit"));
		d = await _manager.GetUserAsync("ouro", new UserCredentials("admin", "changeit"));

		Assert.AreEqual("ouro", d.LoginName);
		Assert.AreEqual("ourofull", d.FullName);
		Assert.AreEqual("foo", d.Groups[0]);
		Assert.AreEqual("bar", d.Groups[1]);
	}

	[Test]
	public async System.Threading.Tasks.Task creating_a_user_with_unicode_chars_can_be_readAsync() {
		UserDetails d = null;
		await _manager.CreateUserAsync("码ou£ro码", "ourofull", new[] { "foo", "bar" }, "ou码码ro",
			new UserCredentials("admin", "changeit"));
		d = await _manager.GetUserAsync("码ou£ro码", new UserCredentials("admin", "changeit"));

		Assert.AreEqual("码ou£ro码", d.LoginName);
		Assert.AreEqual("ourofull", d.FullName);
		Assert.AreEqual("foo", d.Groups[0]);
		Assert.AreEqual("bar", d.Groups[1]);
	}
}
