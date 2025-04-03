// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.ClientAPI.SystemData;

namespace EventStore.Core.Tests.ClientAPI.UserManagement;

public abstract class TestWithUser<TLogFormat, TStreamId>  : TestWithNode<TLogFormat, TStreamId>  {
	protected string _username = Guid.NewGuid().ToString();

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		await _manager.CreateUserAsync(_username, "name", new[] { "foo", "admins" }, "password",
			new UserCredentials("admin", "changeit"));
	}
}
