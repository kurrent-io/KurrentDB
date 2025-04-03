// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.SystemData;
using EventStore.Core.Services;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Security;

[Category("ClientAPI"), Category("LongRunning"), Category("Network")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class multiple_role_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var settings = new SystemSettings(
			new StreamAcl(new[] { "user1", "user2" }, new[] { "$admins", "user1" }, new[] { "user1", SystemRoles.All },
				null, null),
			null);
		await Connection.SetSystemSettingsAsync(settings, new UserCredentials("adm", "admpa$$"));
	}

	[Test]
	public async Task multiple_roles_are_handled_correctly() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("usr-stream", null, null));
		await ReadEvent("usr-stream", "user1", "pa$$1");
		await ReadEvent("usr-stream", "user2", "pa$$2");
		await ReadEvent("usr-stream", "adm", "admpa$$");

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("usr-stream", null, null));
		await WriteStream("usr-stream", "user1", "pa$$1");
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("usr-stream", "user2", "pa$$2"));
		await WriteStream("usr-stream", "adm", "admpa$$");

		await DeleteStream("usr-stream1", null, null);
		await DeleteStream("usr-stream2", "user1", "pa$$1");
		await DeleteStream("usr-stream3", "user2", "pa$$2");
		await DeleteStream("usr-stream4", "adm", "admpa$$");
	}
}
