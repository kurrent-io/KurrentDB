// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class create_persistent_subscription_on_existing_stream<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = Guid.NewGuid().ToString();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	protected override Task When() {
		return _conn.AppendToStreamAsync(_stream, ExpectedVersion.Any,
			new EventData(Guid.NewGuid(), "whatever", true, Encoding.UTF8.GetBytes("{'foo' : 2}"), new Byte[0]));
	}

	[Test]
	public async Task the_completion_succeeds() {
		await _conn.CreatePersistentSubscriptionAsync(_stream, "existing", _settings,
			DefaultData.AdminCredentials);
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class create_persistent_subscription_on_non_existing_stream<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = Guid.NewGuid().ToString();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	protected override Task When() => Task.CompletedTask;

	[Test]
	public async Task the_completion_succeeds() {
		await _conn.CreatePersistentSubscriptionAsync(_stream, "nonexistinggroup", _settings,
			DefaultData.AdminCredentials);
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class create_persistent_subscription_on_all_stream<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	protected override Task When() => Task.CompletedTask;

	[Test]
	public async Task the_completion_fails_with_invalid_stream() {
		await AssertEx.ThrowsAsync<InvalidOperationException>(() =>
			_conn.CreatePersistentSubscriptionAsync("$all", "shitbird", _settings, DefaultData.AdminCredentials));
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class create_persistent_subscription_with_too_big_message_timeout<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	protected override Task When() => Task.CompletedTask;

	[Test]
	public void the_build_fails_with_argument_exception() {
		Assert.Throws<ArgumentException>(() =>
			PersistentSubscriptionSettings.Create().WithMessageTimeoutOf(TimeSpan.FromDays(25 * 365)).Build());
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class create_persistent_subscription_with_too_big_checkpoint_after<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	protected override Task When() => Task.CompletedTask;

	[Test]
	public void the_build_fails_with_argument_exception() {
		Assert.Throws<ArgumentException>(() =>
			PersistentSubscriptionSettings.Create().CheckPointAfter(TimeSpan.FromDays(25 * 365)).Build());
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class create_persistent_subscription_with_dont_timeout<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = Guid.NewGuid().ToString();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent()
		.DontTimeoutMessages();

	protected override Task When() => Task.CompletedTask;

	[Test]
	public void the_message_timeout_should_be_zero() {
		Assert.That(_settings.MessageTimeout == TimeSpan.Zero);
	}

	[Test]
	public async Task the_subscription_is_created_without_error() {
		await _conn.CreatePersistentSubscriptionAsync(_stream, "dont-timeout", _settings,
			DefaultData.AdminCredentials);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class create_duplicate_persistent_subscription_group<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = Guid.NewGuid().ToString();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	protected override Task When() {
		return _conn.CreatePersistentSubscriptionAsync(_stream, "group32", _settings, DefaultData.AdminCredentials);
	}

	[Test]
	public async Task the_completion_fails_with_invalid_operation_exception() {
		await AssertEx.ThrowsAsync<InvalidOperationException>(
			() => _conn.CreatePersistentSubscriptionAsync(_stream, "group32", _settings,
				DefaultData.AdminCredentials));
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	can_create_duplicate_persistent_subscription_group_name_on_different_streams<TLogFormat, TStreamId>
	: SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = Guid.NewGuid().ToString();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	protected override Task When() {
		return _conn.CreatePersistentSubscriptionAsync(_stream, "group3211", _settings, DefaultData.AdminCredentials);
	}

	[Test]
	public async Task the_completion_succeeds() {
		await
			_conn.CreatePersistentSubscriptionAsync("someother" + _stream, "group3211", _settings,
				DefaultData.AdminCredentials);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class create_persistent_subscription_group_without_permissions<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = Guid.NewGuid().ToString();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	protected override Task When() => Task.CompletedTask;

	[Test]
	public async Task the_completion_succeeds() {
		await AssertEx.ThrowsAsync<AccessDeniedException>(() =>
			_conn.CreatePersistentSubscriptionAsync(_stream, "group57", _settings, null));
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class create_persistent_subscription_after_deleting_the_same<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = Guid.NewGuid().ToString();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	protected override async Task When() {
		await _conn.AppendToStreamAsync(_stream, ExpectedVersion.Any,
			new EventData(Guid.NewGuid(), "whatever", true, Encoding.UTF8.GetBytes("{'foo' : 2}"), new Byte[0]));
		await _conn.CreatePersistentSubscriptionAsync(_stream, "existing", _settings, DefaultData.AdminCredentials);
		await _conn.DeletePersistentSubscriptionAsync(_stream, "existing", DefaultData.AdminCredentials);
	}

	[Test]
	public async Task the_completion_succeeds() {
		await _conn.CreatePersistentSubscriptionAsync(_stream, "existing", _settings, DefaultData.AdminCredentials);
	}
}

//ALL
/*

	[TestFixture, Category("LongRunning")]
	public class create_persistent_subscription_on_all : SpecificationWithMiniNode
	{
		private PersistentSubscriptionCreateResult _result;
		private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettingsBuilder.Create()
																.DoNotResolveLinkTos()
																.StartFromCurrent();

		protected override void When()
		{
			_result = _conn.CreatePersistentSubscriptionForAllAsync("group", _settings, DefaultData.AdminCredentials).Result;
		}

		[Test]
		public void the_completion_succeeds()
		{
			Assert.AreEqual(PersistentSubscriptionCreateStatus.Success, _result.Status);
		}
	}


	[TestFixture, Category("LongRunning")]
	public class create_duplicate_persistent_subscription_group_on_all : SpecificationWithMiniNode
	{
		private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettingsBuilder.Create()
																.DoNotResolveLinkTos()
																.StartFromCurrent();
		protected override void When()
		{
			_conn.CreatePersistentSubscriptionForAllAsync("group32", _settings, DefaultData.AdminCredentials).Wait();
		}

		[Test]
		public void the_completion_fails_with_invalid_operation_exception()
		{
			try
			{
				_conn.CreatePersistentSubscriptionForAllAsync("group32", _settings, DefaultData.AdminCredentials).Wait();
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
	public class create_persistent_subscription_group_on_all_without_permissions : SpecificationWithMiniNode
	{
		private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettingsBuilder.Create()
																.DoNotResolveLinkTos()
																.StartFromCurrent();
		protected override void When()
		{
		}

		[Test]
		public void the_completion_succeeds()
		{
			try
			{
				_conn.CreatePersistentSubscriptionForAllAsync("group57", _settings, null).Wait();
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
