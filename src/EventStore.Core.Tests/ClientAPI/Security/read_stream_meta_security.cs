// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI.Exceptions;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Security;

[Category("ClientAPI"), Category("LongRunning"), Category("Network")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class read_stream_meta_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[Test]
	public async Task reading_stream_meta_with_not_existing_credentials_is_not_authenticated() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadMeta("metaread-stream", "badlogin", "badpass"));
	}

	[Test]
	public async Task reading_stream_meta_with_no_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadMeta("metaread-stream", null, null));
	}

	[Test]
	public async Task reading_stream_meta_with_not_authorized_user_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadMeta("metaread-stream", "user2", "pa$$2"));
	}

	[Test]
	public async Task reading_stream_meta_with_authorized_user_credentials_succeeds() {
		await ReadMeta("metaread-stream", "user1", "pa$$1");
	}

	[Test]
	public async Task reading_stream_meta_with_admin_user_credentials_succeeds() {
		await ReadMeta("metaread-stream", "adm", "admpa$$");
	}


	[Test]
	public async Task reading_no_acl_stream_meta_succeeds_when_no_credentials_are_passed() {
		await ReadMeta("noacl-stream", null, null);
	}

	[Test]
	public async Task reading_no_acl_stream_meta_is_not_authenticated_when_not_existing_credentials_are_passed() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadMeta("noacl-stream", "badlogin", "badpass"));
	}

	[Test]
	public async Task reading_no_acl_stream_meta_succeeds_when_any_existing_user_credentials_are_passed() {
		await ReadMeta("noacl-stream", "user1", "pa$$1");
		await ReadMeta("noacl-stream", "user2", "pa$$2");
	}

	[Test]
	public async Task reading_no_acl_stream_meta_succeeds_when_admin_user_credentials_are_passed() {
		await ReadMeta("noacl-stream", "adm", "admpa$$");
	}


	[Test]
	public async Task reading_all_access_normal_stream_meta_succeeds_when_no_credentials_are_passed() {
		await ReadMeta("normal-all", null, null);
	}

	[Test]
	public async Task
		reading_all_access_normal_stream_meta_is_not_authenticated_when_not_existing_credentials_are_passed() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadMeta("normal-all", "badlogin", "badpass"));
	}

	[Test]
	public async Task reading_all_access_normal_stream_meta_succeeds_when_any_existing_user_credentials_are_passed() {
		await ReadMeta("normal-all", "user1", "pa$$1");
		await ReadMeta("normal-all", "user2", "pa$$2");
	}

	[Test]
	public async Task reading_all_access_normal_stream_meta_succeeds_when_admin_user_credentials_are_passed() {
		await ReadMeta("normal-all", "adm", "admpa$$");
	}
}
