// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.SystemData;
using EventStore.Core.Services;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Security;

[Category("ClientAPI"), Category("LongRunning"), Category("Network")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class system_stream_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[Test]
	public async Task operations_on_system_stream_with_no_acl_set_fail_for_non_admin() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("$system-no-acl", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamForward("$system-no-acl", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamBackward("$system-no-acl", "user1", "pa$$1"));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$system-no-acl", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => TransStart("$system-no-acl", "user1", "pa$$1"));

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart("$system-no-acl", "adm", "admpa$$")).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("user1", "pa$$1"));
			await AssertEx.ThrowsAsync<AccessDeniedException>(() => trans.WriteAsync());
			await AssertEx.ThrowsAsync<AccessDeniedException>(() => trans.CommitAsync());
		}

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadMeta("$system-no-acl", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteMeta("$system-no-acl", "user1", "pa$$1", null));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => SubscribeToStream("$system-no-acl", "user1", "pa$$1"));
	}

	[Test]
	public async Task operations_on_system_stream_with_no_acl_set_succeed_for_admin() {
		await ReadEvent("$system-no-acl", "adm", "admpa$$");
		await ReadStreamForward("$system-no-acl", "adm", "admpa$$");
		await ReadStreamBackward("$system-no-acl", "adm", "admpa$$");

		await WriteStream("$system-no-acl", "adm", "admpa$$");

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			await TransStart("$system-no-acl", "adm", "admpa$$");
		}

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart("$system-no-acl", "adm", "admpa$$")).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("adm", "admpa$$"));
			await trans.WriteAsync();
			await trans.CommitAsync();
		}

		await ReadMeta("$system-no-acl", "adm", "admpa$$");
		await WriteMeta("$system-no-acl", "adm", "admpa$$", null);

		await SubscribeToStream("$system-no-acl", "adm", "admpa$$");
	}

	[Test]
	public async Task operations_on_system_stream_with_acl_set_to_usual_user_fail_for_not_authorized_user() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("$system-acl", "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamForward("$system-acl", "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamBackward("$system-acl", "user2", "pa$$2"));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$system-acl", "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => TransStart("$system-acl", "user2", "pa$$2"));

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart("$system-acl", "user1", "pa$$1")).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("user2", "pa$$2"));
			await AssertEx.ThrowsAsync<AccessDeniedException>(() => trans.WriteAsync());
			await AssertEx.ThrowsAsync<AccessDeniedException>(() => trans.CommitAsync());
		}

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadMeta("$system-acl", "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteMeta("$system-acl", "user2", "pa$$2", "user1"));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => SubscribeToStream("$system-acl", "user2", "pa$$2"));
	}

	[Test]
	public async Task operations_on_system_stream_with_acl_set_to_usual_user_succeed_for_that_user() {
		await ReadEvent("$system-acl", "user1", "pa$$1");
		await ReadStreamForward("$system-acl", "user1", "pa$$1");
		await ReadStreamBackward("$system-acl", "user1", "pa$$1");

		await WriteStream("$system-acl", "user1", "pa$$1");

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			await TransStart("$system-acl", "user1", "pa$$1");
		}

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart("$system-acl", "adm", "admpa$$")).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("user1", "pa$$1"));
			await trans.WriteAsync();
			await trans.CommitAsync();
		}

		await ReadMeta("$system-acl", "user1", "pa$$1");
		await WriteMeta("$system-acl", "user1", "pa$$1", "user1");

		await SubscribeToStream("$system-acl", "user1", "pa$$1");
	}

	[Test]
	public async Task operations_on_system_stream_with_acl_set_to_usual_user_succeed_for_admin() {
		await ReadEvent("$system-acl", "adm", "admpa$$");
		await ReadStreamForward("$system-acl", "adm", "admpa$$");
		await ReadStreamBackward("$system-acl", "adm", "admpa$$");

		await WriteStream("$system-acl", "adm", "admpa$$");

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			await TransStart("$system-acl", "adm", "admpa$$");
		}

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart("$system-acl", "user1", "pa$$1")).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("adm", "admpa$$"));
			await trans.WriteAsync();
			await trans.CommitAsync();
		}

		await ReadMeta("$system-acl", "adm", "admpa$$");
		await WriteMeta("$system-acl", "adm", "admpa$$", "user1");

		await SubscribeToStream("$system-acl", "adm", "admpa$$");
	}


	[Test]
	public async Task operations_on_system_stream_with_acl_set_to_admins_fail_for_usual_user() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("$system-adm", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamForward("$system-adm", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamBackward("$system-adm", "user1", "pa$$1"));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("$system-adm", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => TransStart("$system-adm", "user1", "pa$$1"));

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart("$system-adm", "adm", "admpa$$")).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("user1", "pa$$1"));
			await AssertEx.ThrowsAsync<AccessDeniedException>(() => trans.WriteAsync());
			await AssertEx.ThrowsAsync<AccessDeniedException>(() => trans.CommitAsync());
		}

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadMeta("$system-adm", "user1", "pa$$1"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteMeta("$system-adm", "user1", "pa$$1", SystemRoles.Admins));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => SubscribeToStream("$system-adm", "user1", "pa$$1"));
	}

	[Test]
	public async Task operations_on_system_stream_with_acl_set_to_admins_succeed_for_admin() {
		await ReadEvent("$system-adm", "adm", "admpa$$");
		await ReadStreamForward("$system-adm", "adm", "admpa$$");
		await ReadStreamBackward("$system-adm", "adm", "admpa$$");

		await WriteStream("$system-adm", "adm", "admpa$$");

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			await TransStart("$system-adm", "adm", "admpa$$");
		}

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart("$system-adm", "adm", "admpa$$")).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("adm", "admpa$$"));
			await trans.WriteAsync();
			await trans.CommitAsync();
		}

		await ReadMeta("$system-adm", "adm", "admpa$$");
		await WriteMeta("$system-adm", "adm", "admpa$$", SystemRoles.Admins);

		await SubscribeToStream("$system-adm", "adm", "admpa$$");
	}


	[Test]
	public async Task operations_on_system_stream_with_acl_set_to_all_succeed_for_not_authenticated_user() {
		await ReadEvent("$system-all", null, null);
		await ReadStreamForward("$system-all", null, null);
		await ReadStreamBackward("$system-all", null, null);

		await WriteStream("$system-all", null, null);

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			await TransStart("$system-all", null, null);
		}

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart("$system-all", null, null)).TransactionId;
			var trans = Connection.ContinueTransaction(transId);
			await trans.WriteAsync();
			await trans.CommitAsync();
		}

		await ReadMeta("$system-all", null, null);
		await WriteMeta("$system-all", null, null, SystemRoles.All);

		await SubscribeToStream("$system-all", null, null);
	}

	[Test]
	public async Task operations_on_system_stream_with_acl_set_to_all_succeed_for_usual_user() {
		await ReadEvent("$system-all", "user1", "pa$$1");
		await ReadStreamForward("$system-all", "user1", "pa$$1");
		await ReadStreamBackward("$system-all", "user1", "pa$$1");

		await WriteStream("$system-all", "user1", "pa$$1");

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			await TransStart("$system-all", "user1", "pa$$1");
		}

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart("$system-all", "user1", "pa$$1")).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("user1", "pa$$1"));
			await trans.WriteAsync();
			await trans.CommitAsync();
		}

		await ReadMeta("$system-all", "user1", "pa$$1");
		await WriteMeta("$system-all", "user1", "pa$$1", SystemRoles.All);

		await SubscribeToStream("$system-all", "user1", "pa$$1");
	}

	[Test]
	public async Task operations_on_system_stream_with_acl_set_to_all_succeed_for_admin() {
		await ReadEvent("$system-all", "adm", "admpa$$");
		await ReadStreamForward("$system-all", "adm", "admpa$$");
		await ReadStreamBackward("$system-all", "adm", "admpa$$");

		await WriteStream("$system-all", "adm", "admpa$$");

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			await TransStart("$system-all", "adm", "admpa$$");
		}

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart("$system-all", "adm", "admpa$$")).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("adm", "admpa$$"));
			await trans.WriteAsync();
			await trans.CommitAsync();
		}

		await ReadMeta("$system-all", "adm", "admpa$$");
		await WriteMeta("$system-all", "adm", "admpa$$", SystemRoles.All);

		await SubscribeToStream("$system-all", "adm", "admpa$$");
	}
}
