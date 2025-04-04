// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI.Exceptions;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Security;

[Category("ClientAPI"), Category("LongRunning"), Category("Network")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class write_stream_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[Test]
	public async Task writing_to_all_is_never_allowed() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$all", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$all", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$all", "adm", "admpa$$"));
	}

	[Test]
	public async Task writing_with_not_existing_credentials_is_not_authenticated() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => WriteStream("write-stream", "badlogin", "badpass"));
	}

	[Test]
	public async Task writing_to_stream_with_no_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("write-stream", null, null));
	}

	[Test]
	public async Task writing_to_stream_with_not_authorized_user_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("write-stream", "user2", "pa$$2"));
	}

	[Test]
	public async Task writing_to_stream_with_authorized_user_credentials_succeeds() {
		await WriteStream("write-stream", "user1", "pa$$1");
	}

	[Test]
	public async Task writing_to_stream_with_admin_user_credentials_succeeds() {
		await WriteStream("write-stream", "adm", "admpa$$");
	}


	[Test]
	public async Task writing_to_no_acl_stream_succeeds_when_no_credentials_are_passed() {
		await WriteStream("noacl-stream", null, null);
	}

	[Test]
	public async Task writing_to_no_acl_stream_is_not_authenticated_when_not_existing_credentials_are_passed() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => WriteStream("noacl-stream", "badlogin", "badpass"));
	}

	[Test]
	public async Task writing_to_no_acl_stream_succeeds_when_any_existing_user_credentials_are_passed() {
		await WriteStream("noacl-stream", "user1", "pa$$1");
		await WriteStream("noacl-stream", "user2", "pa$$2");
	}

	[Test]
	public async Task writing_to_no_acl_stream_succeeds_when_any_admin_user_credentials_are_passed() {
		await WriteStream("noacl-stream", "adm", "admpa$$");
	}


	[Test]
	public async Task writing_to_all_access_normal_stream_succeeds_when_no_credentials_are_passed() {
		await WriteStream("normal-all", null, null);
	}

	[Test]
	public async Task
		writing_to_all_access_normal_stream_is_not_authenticated_when_not_existing_credentials_are_passed() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => WriteStream("normal-all", "badlogin", "badpass"));
	}

	[Test]
	public async Task writing_to_all_access_normal_stream_succeeds_when_any_existing_user_credentials_are_passed() {
		await WriteStream("normal-all", "user1", "pa$$1");
		await WriteStream("normal-all", "user2", "pa$$2");
	}

	[Test]
	public async Task writing_to_all_access_normal_stream_succeeds_when_any_admin_user_credentials_are_passed() {
		await WriteStream("normal-all", "adm", "admpa$$");
	}
}
