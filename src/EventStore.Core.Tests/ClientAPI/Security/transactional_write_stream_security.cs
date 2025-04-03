// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.SystemData;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Security;

[Category("ClientAPI"), Category("LongRunning"), Category("Network")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class transactional_write_stream_security<TLogFormat, TStreamId> : AuthenticationTestBase<TLogFormat, TStreamId> {
	[Test]
	public async Task starting_transaction_with_not_existing_credentials_is_not_authenticated() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => TransStart("write-stream", "badlogin", "badpass"));
	}

	[Test]
	public async Task starting_transaction_to_stream_with_no_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => TransStart("write-stream", null, null));
	}

	[Test]
	public async Task starting_transaction_to_stream_with_not_authorized_user_credentials_is_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => TransStart("write-stream", "user2", "pa$$2"));
	}

	[Test]
	public async Task starting_transaction_to_stream_with_authorized_user_credentials_succeeds() {
		await TransStart("write-stream", "user1", "pa$$1");
	}

	[Test]
	public async Task starting_transaction_to_stream_with_admin_user_credentials_succeeds() {
		await TransStart("write-stream", "adm", "admpa$$");
	}


	[Test]
	public async Task committing_transaction_with_not_existing_credentials_is_not_authenticated() {
		var t1 = await TransStart("write-stream", "user1", "pa$$1");
		await t1.WriteAsync(CreateEvents());
		var t2 = Connection.ContinueTransaction(t1.TransactionId, new UserCredentials("badlogin", "badpass"));
		
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => t2.CommitAsync());
	}

	[Test]
	public async Task committing_transaction_to_stream_with_no_credentials_is_denied() {
		var t1 = await TransStart("write-stream", "user1", "pa$$1");
		await t1.WriteAsync();
		var t2 = Connection.ContinueTransaction(t1.TransactionId);
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => t2.CommitAsync());
	}

	[Test]
	public async Task committing_transaction_to_stream_with_not_authorized_user_credentials_is_denied() {
		var t1 = await TransStart("write-stream", "user1", "pa$$1");
		await t1.WriteAsync();
		var t2 = Connection.ContinueTransaction(t1.TransactionId, new UserCredentials("user2", "pa$$2"));
		await AssertEx.ThrowsAsync<AccessDeniedException>(() => t2.CommitAsync());
	}

	[Test]
	public async Task committing_transaction_to_stream_with_authorized_user_credentials_succeeds() {
		var transId = (await TransStart("write-stream", "user1", "pa$$1")).TransactionId;
		var t2 = Connection.ContinueTransaction(transId, new UserCredentials("user1", "pa$$1"));
		await t2.WriteAsync();
		await t2.CommitAsync();
	}

	[Test]
	public async Task committing_transaction_to_stream_with_admin_user_credentials_succeeds() {
		var transId = (await TransStart("write-stream", "user1", "pa$$1")).TransactionId;
		var t2 = Connection.ContinueTransaction(transId, new UserCredentials("adm", "admpa$$"));
		await t2.WriteAsync();
		await t2.CommitAsync();
	}


	[Test]
	public void transaction_to_no_acl_stream_succeeds_when_no_credentials_are_passed() {
		ExpectNoException(async () => {
			var t = await TransStart("noacl-stream", null, null);
			await t.WriteAsync(CreateEvents());
			await t.CommitAsync();
		});
	}

	[Test]
	public async Task transaction_to_no_acl_stream_is_not_authenticated_when_not_existing_credentials_are_passed() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => TransStart("noacl-stream", "badlogin", "badpass"));
	}

	[Test]
	public void transaction_to_no_acl_stream_succeeds_when_any_existing_user_credentials_are_passed() {
		ExpectNoException(async () => {
			var t = await TransStart("noacl-stream", "user1", "pa$$1");
			await t.WriteAsync(CreateEvents());
			await t.CommitAsync();
		});
		ExpectNoException(async () => {
			var t = await TransStart("noacl-stream", "user2", "pa$$2");
			await t.WriteAsync(CreateEvents());
			await t.CommitAsync();
		});
	}

	[Test]
	public void transaction_to_no_acl_stream_succeeds_when_admin_user_credentials_are_passed() {
		ExpectNoException(async () => {
			var t = await TransStart("noacl-stream", "adm", "admpa$$");
			await t.WriteAsync(CreateEvents());
			await t.CommitAsync();
		});
	}


	[Test]
	public void transaction_to_all_access_normal_stream_succeeds_when_no_credentials_are_passed() {
		ExpectNoException(async () => {
			var t = await TransStart("normal-all", null, null);
			await t.WriteAsync(CreateEvents());
			await t.CommitAsync();
		});
	}

	[Test]
	public async Task
		transaction_to_all_access_normal_stream_is_not_authenticated_when_not_existing_credentials_are_passed() {
		await AssertEx.ThrowsAsync<NotAuthenticatedException>(() => TransStart("normal-all", "badlogin", "badpass"));
	}

	[Test]
	public void transaction_to_all_access_normal_stream_succeeds_when_any_existing_user_credentials_are_passed() {
		ExpectNoException(async () => {
			var t = await TransStart("normal-all", "user1", "pa$$1");
			await t.WriteAsync(CreateEvents());
			await t.CommitAsync();
		});
		ExpectNoException(async () => {
			var t = await TransStart("normal-all", "user2", "pa$$2");
			await t.WriteAsync(CreateEvents());
			await t.CommitAsync();
		});
	}

	[Test]
	public void transaction_to_all_access_normal_stream_succeeds_when_admin_user_credentials_are_passed() {
		ExpectNoException(async () => {
			var t = await TransStart("normal-all", "adm", "admpa$$");
			await t.WriteAsync(CreateEvents());
			await t.CommitAsync();
		});
	}
}
