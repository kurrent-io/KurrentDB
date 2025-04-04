// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI.Exceptions;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Security;

[Category("ClientAPI"), Category("LongRunning"), Category("Network")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class read_all_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[Test]
	public async Task reading_all_with_not_existing_credentials_is_not_authenticated() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadAllForward("badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadAllBackward("badlogin", "badpass"));
	}

	[Test]
	public async Task reading_all_with_no_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadAllForward(null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadAllBackward(null, null));
	}

	[Test]
	public async Task reading_all_with_not_authorized_user_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadAllForward("user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadAllBackward("user2", "pa$$2"));
	}

	[Test]
	public async Task reading_all_with_authorized_user_credentials_succeeds() {
		await ReadAllForward("user1", "pa$$1");
		await ReadAllBackward("user1", "pa$$1");
	}

	[Test]
	public async Task reading_all_with_admin_credentials_succeeds() {
		await ReadAllForward("adm", "admpa$$");
		await ReadAllBackward("adm", "admpa$$");
	}
}
