// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.SystemData;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Security;

[Category("ClientAPI"), Category("LongRunning"), Category("Network")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class authorized_default_credentials_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	public authorized_default_credentials_security() : base(new UserCredentials("user1", "pa$$1")) {
	}

	[Test]
	public async Task all_operations_succeeds_when_passing_no_explicit_credentials() {
		await ReadAllForward(null, null);
		await ReadAllBackward(null, null);

		await ReadEvent("read-stream", null, null);
		await ReadStreamForward("read-stream", null, null);
		await ReadStreamBackward("read-stream", null, null);

		await WriteStream("write-stream", null, null);

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			await ExpectNoException(async () => {
				var trans = await TransStart("write-stream", null, null);
				await trans.WriteAsync();
				await trans.CommitAsync();
			});
		}

		await ReadMeta("metaread-stream", null, null);
		await WriteMeta("metawrite-stream", null, null, "user1");

		await SubscribeToStream("read-stream", null, null);
		await SubscribeToAll(null, null);
	}

	[Test]
	public async Task all_operations_are_not_authenticated_when_overriden_with_not_existing_credentials() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadAllForward("badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadAllBackward("badlogin", "badpass"));

		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadEvent("read-stream", "badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadStreamForward("read-stream", "badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadStreamBackward("read-stream", "badlogin", "badpass"));

		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => WriteStream("write-stream", "badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => TransStart("write-stream", "badlogin", "badpass"));

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart("write-stream", null, null)).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("badlogin", "badpass"));
			await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => trans.WriteAsync());
			await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => trans.CommitAsync());
		}

		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => ReadMeta("metaread-stream", "badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => WriteMeta("metawrite-stream", "badlogin", "badpass", "user1"));

		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => SubscribeToStream("read-stream", "badlogin", "badpass"));
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => SubscribeToAll("badlogin", "badpass"));
	}

	[Test]
	public async Task all_operations_are_not_authorized_when_overriden_with_not_authorized_credentials() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadAllForward("user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadAllBackward("user2", "pa$$2"));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadEvent("read-stream", "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamForward("read-stream", "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadStreamBackward("read-stream", "user2", "pa$$2"));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteStream("write-stream", "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => TransStart("write-stream", "user2", "pa$$2"));

		if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
			var transId = (await TransStart("write-stream", null, null)).TransactionId;
			var trans = Connection.ContinueTransaction(transId, new UserCredentials("user2", "pa$$2"));
			await AssertEx.ThrowsAsync<AccessDeniedException>(() => trans.WriteAsync());
			await AssertEx.ThrowsAsync<AccessDeniedException>(() => trans.CommitAsync());
		}

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => ReadMeta("metaread-stream", "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => WriteMeta("metawrite-stream", "user2", "pa$$2", "user1"));

		await AssertEx.ThrowsAsync<AccessDeniedException>(() => SubscribeToStream("read-stream", "user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => SubscribeToAll("user2", "pa$$2"));
	}
}
