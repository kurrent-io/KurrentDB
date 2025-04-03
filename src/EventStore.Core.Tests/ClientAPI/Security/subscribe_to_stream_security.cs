// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI.Exceptions;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Security;

[Category("ClientAPI"), Category("LongRunning"), Category("Network")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class subscribe_to_stream_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[Test]
	public async Task subscribing_to_stream_with_not_existing_credentials_is_not_authenticated() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => SubscribeToStream("read-stream", "badlogin", "badpass"));
	}

	[Test]
	public async Task subscribing_to_stream_with_no_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => SubscribeToStream("read-stream", null, null));
	}

	[Test]
	public async Task subscribing_to_stream_with_not_authorized_user_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => SubscribeToStream("read-stream", "user2", "pa$$2"));
	}

	[Test]
	public async Task reading_stream_with_authorized_user_credentials_succeeds() {
		await SubscribeToStream("read-stream", "user1", "pa$$1");
	}

	[Test]
	public async Task reading_stream_with_admin_user_credentials_succeeds() {
		await SubscribeToStream("read-stream", "adm", "admpa$$");
	}


	[Test]
	public async Task subscribing_to_no_acl_stream_succeeds_when_no_credentials_are_passed() {
		await SubscribeToStream("noacl-stream", null, null);
	}

	[Test]
	public async Task subscribing_to_no_acl_stream_is_not_authenticated_when_not_existing_credentials_are_passed() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => SubscribeToStream("noacl-stream", "badlogin", "badpass"));
	}

	[Test]
	public async Task subscribing_to_no_acl_stream_succeeds_when_any_existing_user_credentials_are_passed() {
		await SubscribeToStream("noacl-stream", "user1", "pa$$1");
		await SubscribeToStream("noacl-stream", "user2", "pa$$2");
	}

	[Test]
	public async Task subscribing_to_no_acl_stream_succeeds_when_admin_user_credentials_are_passed() {
		await SubscribeToStream("noacl-stream", "adm", "admpa$$");
	}


	[Test]
	public async Task subscribing_to_all_access_normal_stream_succeeds_when_no_credentials_are_passed() {
		await SubscribeToStream("normal-all", null, null);
	}

	[Test]
	public async Task
		subscribing_to_all_access_normal_stream_is_not_authenticated_when_not_existing_credentials_are_passed() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => SubscribeToStream("normal-all", "badlogin", "badpass"));
	}

	[Test]
	public async Task subscribing_to_all_access_normal_stream_succeeds_when_any_existing_user_credentials_are_passed() {
		await SubscribeToStream("normal-all", "user1", "pa$$1");
		await SubscribeToStream("normal-all", "user2", "pa$$2");
	}

	[Test]
	public async Task subscribing_to_all_access_normal_streamm_succeeds_when_admin_user_credentials_are_passed() {
		await SubscribeToStream("normal-all", "adm", "admpa$$");
	}
}
