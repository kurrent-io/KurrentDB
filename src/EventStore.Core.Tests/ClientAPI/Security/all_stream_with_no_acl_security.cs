// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.SystemData;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Security;

[Category("ClientAPI"), Category("LongRunning"), Category("Network")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class all_stream_with_no_acl_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		await Connection.SetStreamMetadataAsync("$all", ExpectedVersion.Any, StreamMetadata.Build(),
			new UserCredentials("adm", "admpa$$"));
	}

	[Test]
	public async Task write_to_all_is_never_allowed() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$all", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$all", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$all", "adm", "admpa$$"));
	}

	[Test]
	public async Task delete_of_all_is_never_allowed() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream("$all", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream("$all", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream("$all", "adm", "admpa$$"));
	}


	[Test]
	public async Task reading_and_subscribing_is_not_allowed_when_no_credentials_are_passed() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("$all", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamForward("$all", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamBackward("$all", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadMeta("$all", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => SubscribeToStream("$all", null, null));
	}

	[Test]
	public async Task reading_and_subscribing_is_not_allowed_for_usual_user() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("$all", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamForward("$all", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamBackward("$all", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadMeta("$all", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => SubscribeToStream("$all", "user1", "pa$$1"));
	}

	[Test]
	public async Task reading_and_subscribing_is_allowed_for_admin_user() {
		await ReadEvent("$all", "adm", "admpa$$");
		await ReadStreamForward("$all", "adm", "admpa$$");
		await ReadStreamBackward("$all", "adm", "admpa$$");
		await ReadMeta("$all", "adm", "admpa$$");
		await SubscribeToStream("$all", "adm", "admpa$$");
	}


	[Test]
	public async Task meta_write_is_not_allowed_when_no_credentials_are_passed() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteMeta("$all", null, null, null));
	}

	[Test]
	public async Task meta_write_is_not_allowed_for_usual_user() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteMeta("$all", "user1", "pa$$1", null));
	}

	[Test]
	public async Task meta_write_is_allowed_for_admin_user() {
		await WriteMeta("$all", "adm", "admpa$$", null);
	}
}
