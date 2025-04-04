// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.ClientAPI.SystemData;

namespace EventStore.Core.Tests.ClientAPI.UserManagement;

public abstract class TestWithUser<TLogFormat, TStreamId> : TestWithNode<TLogFormat, TStreamId> {
	protected string _username = Guid.NewGuid().ToString();

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		await _manager.CreateUserAsync(_username, "name", new[] { "foo", "admins" }, "password",
			new UserCredentials("admin", "changeit"));
	}
}
