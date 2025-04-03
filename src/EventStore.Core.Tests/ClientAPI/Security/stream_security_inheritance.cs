// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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
public class stream_security_inheritance<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var settings = new SystemSettings(userStreamAcl: new StreamAcl(null, "user1", null, null, null),
			systemStreamAcl: new StreamAcl(null, "user1", null, null, null));
		await Connection.SetSystemSettingsAsync(settings, new UserCredentials("adm", "admpa$$"));

		await Connection.SetStreamMetadataAsync("user-no-acl", ExpectedVersion.NoStream,
			StreamMetadata.Build(), new UserCredentials("adm", "admpa$$"));
		await Connection.SetStreamMetadataAsync("user-w-diff", ExpectedVersion.NoStream,
			StreamMetadata.Build().SetWriteRole("user2"), new UserCredentials("adm", "admpa$$"));
		await Connection.SetStreamMetadataAsync("user-w-multiple", ExpectedVersion.NoStream,
				StreamMetadata.Build().SetWriteRoles(new[] { "user1", "user2" }),
				new UserCredentials("adm", "admpa$$"))
			;
		await Connection.SetStreamMetadataAsync("user-w-restricted", ExpectedVersion.NoStream,
			StreamMetadata.Build().SetWriteRoles(new string[0]), new UserCredentials("adm", "admpa$$"));
		await Connection.SetStreamMetadataAsync("user-w-all", ExpectedVersion.NoStream,
			StreamMetadata.Build().SetWriteRole(SystemRoles.All), new UserCredentials("adm", "admpa$$"));

		await Connection.SetStreamMetadataAsync("user-r-restricted", ExpectedVersion.NoStream,
			StreamMetadata.Build().SetReadRole("user1"), new UserCredentials("adm", "admpa$$"));

		await Connection.SetStreamMetadataAsync("$sys-no-acl", ExpectedVersion.NoStream,
			StreamMetadata.Build(), new UserCredentials("adm", "admpa$$"));
		await Connection.SetStreamMetadataAsync("$sys-w-diff", ExpectedVersion.NoStream,
			StreamMetadata.Build().SetWriteRole("user2"), new UserCredentials("adm", "admpa$$"));
		await Connection.SetStreamMetadataAsync("$sys-w-multiple", ExpectedVersion.NoStream,
				StreamMetadata.Build().SetWriteRoles(new[] { "user1", "user2" }),
				new UserCredentials("adm", "admpa$$"))
			;
		await Connection.SetStreamMetadataAsync("$sys-w-restricted", ExpectedVersion.NoStream,
			StreamMetadata.Build().SetWriteRoles(new string[0]), new UserCredentials("adm", "admpa$$"));
		await Connection.SetStreamMetadataAsync("$sys-w-all", ExpectedVersion.NoStream,
			StreamMetadata.Build().SetWriteRole(SystemRoles.All), new UserCredentials("adm", "admpa$$"));
	}

	[Test]
	public async Task acl_inheritance_is_working_properly_on_user_streams() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("user-no-acl", null, null));
		await WriteStream("user-no-acl", "user1", "pa$$1");
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("user-no-acl", "user2", "pa$$2"));
		await WriteStream("user-no-acl", "adm", "admpa$$");

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("user-w-diff", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("user-w-diff", "user1", "pa$$1"));
		await WriteStream("user-w-diff", "user2", "pa$$2");
		await WriteStream("user-w-diff", "adm", "admpa$$");

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("user-w-multiple", null, null));
		await WriteStream("user-w-multiple", "user1", "pa$$1");
		await WriteStream("user-w-multiple", "user2", "pa$$2");
		await WriteStream("user-w-multiple", "adm", "admpa$$");

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("user-w-restricted", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("user-w-restricted", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("user-w-restricted", "user2", "pa$$2"));
		await WriteStream("user-w-restricted", "adm", "admpa$$");

		await WriteStream("user-w-all", null, null);
		await WriteStream("user-w-all", "user1", "pa$$1");
		await WriteStream("user-w-all", "user2", "pa$$2");
		await WriteStream("user-w-all", "adm", "admpa$$");


		await ReadEvent("user-no-acl", null, null);
		await ReadEvent("user-no-acl", "user1", "pa$$1");
		await ReadEvent("user-no-acl", "user2", "pa$$2");
		await ReadEvent("user-no-acl", "adm", "admpa$$");

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("user-r-restricted", null, null));
		await ReadEvent("user-r-restricted", "user1", "pa$$1");
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("user-r-restricted", "user2", "pa$$2"));
		await ReadEvent("user-r-restricted", "adm", "admpa$$");
	}

	[Test]
	public async Task acl_inheritance_is_working_properly_on_system_streams() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$sys-no-acl", null, null));
		await WriteStream("$sys-no-acl", "user1", "pa$$1");
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$sys-no-acl", "user2", "pa$$2"));
		await WriteStream("$sys-no-acl", "adm", "admpa$$");

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$sys-w-diff", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$sys-w-diff", "user1", "pa$$1"));
		await WriteStream("$sys-w-diff", "user2", "pa$$2");
		await WriteStream("$sys-w-diff", "adm", "admpa$$");

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$sys-w-multiple", null, null));
		await WriteStream("$sys-w-multiple", "user1", "pa$$1");
		await WriteStream("$sys-w-multiple", "user2", "pa$$2");
		await WriteStream("$sys-w-multiple", "adm", "admpa$$");

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$sys-w-restricted", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$sys-w-restricted", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$sys-w-restricted", "user2", "pa$$2"));
		await WriteStream("$sys-w-restricted", "adm", "admpa$$");

		await WriteStream("$sys-w-all", null, null);
		await WriteStream("$sys-w-all", "user1", "pa$$1");
		await WriteStream("$sys-w-all", "user2", "pa$$2");
		await WriteStream("$sys-w-all", "adm", "admpa$$");

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("$sys-no-acl", null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("$sys-no-acl", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("$sys-no-acl", "user2", "pa$$2"));
		await ReadEvent("$sys-no-acl", "adm", "admpa$$");
	}
}
