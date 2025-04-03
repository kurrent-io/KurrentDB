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
public class overriden_system_stream_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var settings = new SystemSettings(userStreamAcl: null,
			systemStreamAcl: new StreamAcl("user1", "user1", "user1", "user1", "user1"));
		await Connection.SetSystemSettingsAsync(settings, new UserCredentials("adm", "admpa$$"));
	}

	[Test]
	public async Task operations_on_system_stream_succeed_for_authorized_user() {
		const string stream = "$sys-authorized-user";
		await ReadEvent(stream, "user1", "pa$$1");
		await ReadStreamForward(stream, "user1", "pa$$1");
		await ReadStreamBackward(stream, "user1", "pa$$1");

		await WriteStream(stream, "user1", "pa$$1");

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			await TransStart(stream, "user1", "pa$$1");
		}

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart(stream, "adm", "admpa$$")).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("user1", "pa$$1"));
			await trans.WriteAsync();
			await trans.CommitAsync();
		}

		await ReadMeta(stream, "user1", "pa$$1");
		await WriteMeta(stream, "user1", "pa$$1", null);

		await SubscribeToStream(stream, "user1", "pa$$1");

		await DeleteStream(stream, "user1", "pa$$1");
	}

	[Test]
	public async Task operations_on_system_stream_fail_for_not_authorized_user() {
		const string stream = "$sys-not-authorized-user";
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent(stream, "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamForward(stream, "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamBackward(stream, "user2", "pa$$2"));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream(stream, "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => TransStart(stream, "user2", "pa$$2"));

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart(stream, "adm", "admpa$$")).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("user2", "pa$$2"));
			await AssertEx.ThrowsAsync<AccessDeniedException>(() => trans.WriteAsync());
			await AssertEx.ThrowsAsync<AccessDeniedException>(() => trans.CommitAsync());
		}

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadMeta(stream, "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteMeta(stream, "user2", "pa$$2", null));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => SubscribeToStream(stream, "user2", "pa$$2"));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream(stream, "user2", "pa$$2"));
	}

	[Test]
	public async Task operations_on_system_stream_fail_for_anonymous_user() {
		const string stream = "$sys-anonymous-user";
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent(stream, null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamForward(stream, null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamBackward(stream, null, null));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream(stream, null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => TransStart(stream, null, null));

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart(stream, "adm", "admpa$$")).TransactionId;
			var trans = Connection.ContinueTransaction(transId);
			await AssertEx.ThrowsAsync<AccessDeniedException>(() => trans.WriteAsync());
			await AssertEx.ThrowsAsync<AccessDeniedException>(() => trans.CommitAsync());
		}

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadMeta(stream, null, null));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteMeta(stream, null, null, null));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => SubscribeToStream(stream, null, null));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => DeleteStream(stream, null, null));
	}

	[Test]
	public async Task operations_on_system_stream_succeed_for_admin() {
		const string stream = "$sys-admin";
		await ReadEvent(stream, "adm", "admpa$$");
		await ReadStreamForward(stream, "adm", "admpa$$");
		await ReadStreamBackward(stream, "adm", "admpa$$");

		await WriteStream(stream, "adm", "admpa$$");

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			await TransStart(stream, "adm", "admpa$$");
		}

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart(stream, "adm", "admpa$$")).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("adm", "admpa$$"));
			await trans.WriteAsync();
			await trans.CommitAsync();
		}

		await ReadMeta(stream, "adm", "admpa$$");
		await WriteMeta(stream, "adm", "admpa$$", null);

		await SubscribeToStream(stream, "adm", "admpa$$");

		await DeleteStream(stream, "adm", "admpa$$");
	}
}
