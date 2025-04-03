// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI.Exceptions;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Security;

[Category("ClientAPI"), Category("LongRunning"), Category("Network")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class read_stream_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[Test]
	public async Task reading_stream_with_not_existing_credentials_is_not_authenticated() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadEvent("read-stream", "badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadStreamForward("read-stream", "badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadStreamBackward("read-stream", "badlogin", "badpass"));
	}

	[Test]
	public async Task reading_stream_with_no_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("read-stream", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamForward("read-stream", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamBackward("read-stream", null, null));
	}

	[Test]
	public async Task reading_stream_with_not_authorized_user_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("read-stream", "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamForward("read-stream", "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamBackward("read-stream", "user2", "pa$$2"));
	}

	[Test]
	public async Task reading_stream_with_authorized_user_credentials_succeeds() {
		await ReadEvent("read-stream", "user1", "pa$$1");
		await ReadStreamForward("read-stream", "user1", "pa$$1");
		await ReadStreamBackward("read-stream", "user1", "pa$$1");
	}

	[Test]
	public async Task reading_stream_with_admin_user_credentials_succeeds() {
		await ReadEvent("read-stream", "adm", "admpa$$");
		await ReadStreamForward("read-stream", "adm", "admpa$$");
		await ReadStreamBackward("read-stream", "adm", "admpa$$");
	}


	[Test]
	public async Task reading_no_acl_stream_succeeds_when_no_credentials_are_passed() {
		await ReadEvent("noacl-stream", null, null);
		await ReadStreamForward("noacl-stream", null, null);
		await ReadStreamBackward("noacl-stream", null, null);
	}

	[Test]
	public async Task reading_no_acl_stream_is_not_authenticated_when_not_existing_credentials_are_passed() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadEvent("noacl-stream", "badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadStreamForward("noacl-stream", "badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadStreamBackward("noacl-stream", "badlogin", "badpass"));
	}

	[Test]
	public async Task reading_no_acl_stream_succeeds_when_any_existing_user_credentials_are_passed() {
		await ReadEvent("noacl-stream", "user1", "pa$$1");
		await ReadStreamForward("noacl-stream", "user1", "pa$$1");
		await ReadStreamBackward("noacl-stream", "user1", "pa$$1");
		await ReadEvent("noacl-stream", "user2", "pa$$2");
		await ReadStreamForward("noacl-stream", "user2", "pa$$2");
		await ReadStreamBackward("noacl-stream", "user2", "pa$$2");
	}

	[Test]
	public async Task reading_no_acl_stream_succeeds_when_admin_user_credentials_are_passed() {
		await ReadEvent("noacl-stream", "adm", "admpa$$");
		await ReadStreamForward("noacl-stream", "adm", "admpa$$");
		await ReadStreamBackward("noacl-stream", "adm", "admpa$$");
	}


	[Test]
	public async Task reading_all_access_normal_stream_succeeds_when_no_credentials_are_passed() {
		await ReadEvent("normal-all", null, null);
		await ReadStreamForward("normal-all", null, null);
		await ReadStreamBackward("normal-all", null, null);
	}

	[Test]
	public async Task reading_all_access_normal_stream_is_not_authenticated_when_not_existing_credentials_are_passed() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadEvent("normal-all", "badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadStreamForward("normal-all", "badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadStreamBackward("normal-all", "badlogin", "badpass"));
	}

	[Test]
	public async Task reading_all_access_normal_stream_succeeds_when_any_existing_user_credentials_are_passed() {
		await ReadEvent("normal-all", "user1", "pa$$1");
		await ReadStreamForward("normal-all", "user1", "pa$$1");
		await ReadStreamBackward("normal-all", "user1", "pa$$1");
		await ReadEvent("normal-all", "user2", "pa$$2");
		await ReadStreamForward("normal-all", "user2", "pa$$2");
		await ReadStreamBackward("normal-all", "user2", "pa$$2");
	}

	[Test]
	public async Task reading_all_access_normal_stream_succeeds_when_admin_user_credentials_are_passed() {
		await ReadEvent("normal-all", "adm", "admpa$$");
		await ReadStreamForward("normal-all", "adm", "admpa$$");
		await ReadStreamBackward("normal-all", "adm", "admpa$$");
	}
}
