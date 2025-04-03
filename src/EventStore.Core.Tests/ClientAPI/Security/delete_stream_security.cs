// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using EventStore.Core.Services;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Security;

[Category("ClientAPI"), Category("LongRunning"), Category("Network")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class delete_stream_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[Test]
	public async Task delete_of_all_is_never_allowed() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream("$all", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream("$all", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream("$all", "adm", "admpa$$"));
	}

	[Test]
	public async Task deleting_normal_no_acl_stream_with_no_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build());
		await DeleteStream(streamId, null, null);
	}

	[Test]
	public async Task deleting_normal_no_acl_stream_with_existing_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build());
		await DeleteStream(streamId, "user1", "pa$$1");
	}

	[Test]
	public async Task deleting_normal_no_acl_stream_with_admin_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build());
		await DeleteStream(streamId, "adm", "admpa$$");
	}


	[Test]
	public async Task deleting_normal_user_stream_with_no_user_is_not_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build().SetDeleteRole("user1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream(streamId, null, null));
	}

	[Test]
	public async Task deleting_normal_user_stream_with_not_authorized_user_is_not_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build().SetDeleteRole("user1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream(streamId, "user2", "pa$$2"));
	}

	[Test]
	public async Task deleting_normal_user_stream_with_authorized_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build().SetDeleteRole("user1"));
		await DeleteStream(streamId, "user1", "pa$$1");
	}

	[Test]
	public async Task deleting_normal_user_stream_with_admin_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build().SetDeleteRole("user1"));
		await DeleteStream(streamId, "adm", "admpa$$");
	}


	[Test]
	public async Task deleting_normal_admin_stream_with_no_user_is_not_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build().SetDeleteRole(SystemRoles.Admins));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream(streamId, null, null));
	}

	[Test]
	public async Task deleting_normal_admin_stream_with_existing_user_is_not_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build().SetDeleteRole(SystemRoles.Admins));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream(streamId, "user1", "pa$$1"));
	}

	[Test]
	public async Task deleting_normal_admin_stream_with_admin_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build().SetDeleteRole(SystemRoles.Admins));
		await DeleteStream(streamId, "adm", "admpa$$");
	}


	[Test]
	public async Task deleting_normal_all_stream_with_no_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build().SetDeleteRole(SystemRoles.All));
		await DeleteStream(streamId, null, null);
	}

	[Test]
	public async Task deleting_normal_all_stream_with_existing_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build().SetDeleteRole(SystemRoles.All));
		await DeleteStream(streamId, "user1", "pa$$1");
	}

	[Test]
	public async Task deleting_normal_all_stream_with_admin_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(StreamMetadata.Build().SetDeleteRole(SystemRoles.All));
		await DeleteStream(streamId, "adm", "admpa$$");
	}

	// $-stream

	[Test]
	public async Task deleting_system_no_acl_stream_with_no_user_is_not_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$", metadata: StreamMetadata.Build());
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream(streamId, null, null));
	}

	[Test]
	public async Task deleting_system_no_acl_stream_with_existing_user_is_not_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$", metadata: StreamMetadata.Build());
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream(streamId, "user1", "pa$$1"));
	}

	[Test]
	public async Task deleting_system_no_acl_stream_with_admin_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$", metadata: StreamMetadata.Build());
		await DeleteStream(streamId, "adm", "admpa$$");
	}


	[Test]
	public async Task deleting_system_user_stream_with_no_user_is_not_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$",
			metadata: StreamMetadata.Build().SetDeleteRole("user1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream(streamId, null, null));
	}

	[Test]
	public async Task deleting_system_user_stream_with_not_authorized_user_is_not_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$",
			metadata: StreamMetadata.Build().SetDeleteRole("user1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream(streamId, "user2", "pa$$2"));
	}

	[Test]
	public async Task deleting_system_user_stream_with_authorized_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$",
			metadata: StreamMetadata.Build().SetDeleteRole("user1"));
		await DeleteStream(streamId, "user1", "pa$$1");
	}

	[Test]
	public async Task deleting_system_user_stream_with_admin_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$",
			metadata: StreamMetadata.Build().SetDeleteRole("user1"));
		await DeleteStream(streamId, "adm", "admpa$$");
	}


	[Test]
	public async Task deleting_system_admin_stream_with_no_user_is_not_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$",
			metadata: StreamMetadata.Build().SetDeleteRole(SystemRoles.Admins));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream(streamId, null, null));
	}

	[Test]
	public async Task deleting_system_admin_stream_with_existing_user_is_not_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$",
			metadata: StreamMetadata.Build().SetDeleteRole(SystemRoles.Admins));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream(streamId, "user1", "pa$$1"));
	}

	[Test]
	public async Task deleting_system_admin_stream_with_admin_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$",
			metadata: StreamMetadata.Build().SetDeleteRole(SystemRoles.Admins));
		await DeleteStream(streamId, "adm", "admpa$$");
	}


	[Test]
	public async Task deleting_system_all_stream_with_no_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$",
			metadata: StreamMetadata.Build().SetDeleteRole(SystemRoles.All));
		await DeleteStream(streamId, null, null);
	}

	[Test]
	public async Task deleting_system_all_stream_with_existing_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$",
			metadata: StreamMetadata.Build().SetDeleteRole(SystemRoles.All));
		await DeleteStream(streamId, "user1", "pa$$1");
	}

	[Test]
	public async Task deleting_system_all_stream_with_admin_user_is_allowed() {
		var streamId = await CreateStreamWithMeta(streamPrefix: "$",
			metadata: StreamMetadata.Build().SetDeleteRole(SystemRoles.All));
		await DeleteStream(streamId, "adm", "admpa$$");
	}
}
