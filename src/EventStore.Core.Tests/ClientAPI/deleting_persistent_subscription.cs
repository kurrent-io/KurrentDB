// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class deleting_existing_persistent_subscription_group_with_permissions<TLogFormat, TStreamId>
	: SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	private readonly string _stream = Guid.NewGuid().ToString();

	protected override Task When() =>
		_conn.CreatePersistentSubscriptionAsync(_stream, "groupname123", _settings,
			DefaultData.AdminCredentials);

	[Test]
	public async Task the_delete_of_group_succeeds() {
		await _conn.DeletePersistentSubscriptionAsync(_stream, "groupname123", DefaultData.AdminCredentials);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class deleting_existing_persistent_subscription_with_subscriber<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	private readonly string _stream = Guid.NewGuid().ToString();
	private readonly ManualResetEvent _called = new ManualResetEvent(false);

	protected override async Task Given() {
		await base.Given();
		await _conn.CreatePersistentSubscriptionAsync(_stream, "groupname123", _settings,
			DefaultData.AdminCredentials);
		_conn.ConnectToPersistentSubscription(_stream, "groupname123",
			(s, e) => Task.CompletedTask,
			(s, r, e) => _called.Set(), DefaultData.AdminCredentials);
	}

	protected override Task When() {
		return _conn.DeletePersistentSubscriptionAsync(_stream, "groupname123", DefaultData.AdminCredentials);
	}

	[Test]
	public void the_subscription_is_dropped() {
		Assert.IsTrue(_called.WaitOne(TimeSpan.FromSeconds(5)));
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class deleting_persistent_subscription_group_that_doesnt_exist<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = Guid.NewGuid().ToString();

	protected override Task When() => Task.CompletedTask;

	[Test]
	public async Task the_delete_fails_with_argument_exception() {
		await AssertEx.ThrowsAsync<InvalidOperationException>(
			() =>
				_conn.DeletePersistentSubscriptionAsync(_stream, Guid.NewGuid().ToString(),
					DefaultData.AdminCredentials));
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class deleting_persistent_subscription_group_without_permissions<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = Guid.NewGuid().ToString();

	protected override Task When() => Task.CompletedTask;

	[Test]
	public async Task the_delete_fails_with_access_denied() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(
			() => _conn.DeletePersistentSubscriptionAsync(_stream, Guid.NewGuid().ToString()));
	}
}

//ALL
/*

	[TestFixture, Category("LongRunning")]
	public class deleting_existing_persistent_subscription_group_on_all_with_permissions : SpecificationWithMiniNode
	{
		private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettingsBuilder.Create()
																.DoNotResolveLinkTos()
																.StartFromCurrent();
		protected override void When()
		{
			_conn.CreatePersistentSubscriptionForAllAsync("groupname123", _settings,
				DefaultData.AdminCredentials).Wait();
		}

		[Test]
		public void the_delete_of_group_succeeds()
		{
			var result = _conn.DeletePersistentSubscriptionForAllAsync("groupname123", DefaultData.AdminCredentials).Result;
			Assert.AreEqual(PersistentSubscriptionDeleteStatus.Success, result.Status);
		}
	}

	[TestFixture, Category("LongRunning")]
	public class deleting_persistent_subscription_group_on_all_that_doesnt_exist : SpecificationWithMiniNode
	{
		protected override void When()
		{
		}

		[Test]
		public void the_delete_fails_with_argument_exception()
		{
			try
			{
				_conn.DeletePersistentSubscriptionForAllAsync(Guid.NewGuid().ToString(), DefaultData.AdminCredentials).Wait();
				throw new Exception("expected exception");
			}
			catch (Exception ex)
			{
				Assert.IsInstanceOf(typeof(AggregateException), ex);
				var inner = ex.InnerException;
				Assert.IsInstanceOf(typeof(InvalidOperationException), inner);
			}
		}
	}


	[TestFixture, Category("LongRunning")]
	public class deleting_persistent_subscription_group_on_all_without_permissions : SpecificationWithMiniNode
	{
		protected override void When()
		{
		}

		[Test]
		public void the_delete_fails_with_access_denied()
		{
			try
			{
				_conn.DeletePersistentSubscriptionForAllAsync(Guid.NewGuid().ToString()).Wait();
				throw new Exception("expected exception");
			}
			catch (Exception ex)
			{
				Assert.IsInstanceOf(typeof(AggregateException), ex);
				var inner = ex.InnerException;
				Assert.IsInstanceOf(typeof(AccessDeniedException), inner);
			}
		}
	}
*/
