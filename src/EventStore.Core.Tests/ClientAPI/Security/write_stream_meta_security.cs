// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI.Exceptions;
using EventStore.Core.Services;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Security;

[Category("ClientAPI"), Category("LongRunning"), Category("Network")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class write_stream_meta_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[Test]
	public async Task writing_meta_with_not_existing_credentials_is_not_authenticated() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => WriteMeta("metawrite-stream", "badlogin", "badpass", "user1"));
	}

	[Test]
	public async Task writing_meta_to_stream_with_no_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteMeta("metawrite-stream", null, null, "user1"));
	}

	[Test]
	public async Task writing_meta_to_stream_with_not_authorized_user_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteMeta("metawrite-stream", "user2", "pa$$2", "user1"));
	}

	[Test]
	public async Task writing_meta_to_stream_with_authorized_user_credentials_succeeds() {
		await WriteMeta("metawrite-stream", "user1", "pa$$1", "user1");
	}

	[Test]
	public async Task writing_meta_to_stream_with_admin_user_credentials_succeeds() {
		await WriteMeta("metawrite-stream", "adm", "admpa$$", "user1");
	}


	[Test]
	public async Task writing_meta_to_no_acl_stream_succeeds_when_no_credentials_are_passed() {
		await WriteMeta("noacl-stream", null, null, null);
	}

	[Test]
	public async Task writing_meta_to_no_acl_stream_is_not_authenticated_when_not_existing_credentials_are_passed() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => WriteMeta("noacl-stream", "badlogin", "badpass", null));
	}

	[Test]
	public async Task writing_meta_to_no_acl_stream_succeeds_when_any_existing_user_credentials_are_passed() {
		await WriteMeta("noacl-stream", "user1", "pa$$1", null);
		await WriteMeta("noacl-stream", "user2", "pa$$2", null);
	}

	[Test]
	public async Task writing_meta_to_no_acl_stream_succeeds_when_admin_user_credentials_are_passed() {
		await WriteMeta("noacl-stream", "adm", "admpa$$", null);
	}


	[Test]
	public async Task writing_meta_to_all_access_normal_stream_succeeds_when_no_credentials_are_passed() {
		await WriteMeta("normal-all", null, null, SystemRoles.All);
	}

	[Test]
	public async Task
		writing_meta_to_all_access_normal_stream_is_not_authenticated_when_not_existing_credentials_are_passed() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => WriteMeta("normal-all", "badlogin", "badpass", SystemRoles.All));
	}

	[Test]
	public async Task writing_meta_to_all_access_normal_stream_succeeds_when_any_existing_user_credentials_are_passed() {
		await WriteMeta("normal-all", "user1", "pa$$1", SystemRoles.All);
		await WriteMeta("normal-all", "user2", "pa$$2", SystemRoles.All);
	}

	[Test]
	public async Task writing_meta_to_all_access_normal_stream_succeeds_when_admin_user_credentials_are_passed() {
		await WriteMeta("normal-all", "adm", "admpa$$", SystemRoles.All);
	}
}
