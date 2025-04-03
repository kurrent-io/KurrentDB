// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Metrics;
using EventStore.Core.Services.PersistentSubscription;
using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;
using EventStore.Core.Tests.TransactionLog;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.PersistentSubscription;

[TestFixture]
public class PersistentSubscriptionServiceErrorTests {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public abstract class when_an_error_occurs<TLogFormat, TStreamId, TMessage, TReply, TResult>
		where TMessage : Message where TReply : Message {
		private readonly TResult _expectedResult;
		private readonly PersistentSubscriptionService<TStreamId> _sut;
		private readonly TaskCompletionSource<Message> _replySource;
		private readonly CallbackEnvelope _envelope;

		protected when_an_error_occurs(TResult expectedResult) {
			_expectedResult = expectedResult;
			_replySource = new TaskCompletionSource<Message>();
			var bus = new SynchronousScheduler();
			var trackers = new Trackers();
			_sut = new PersistentSubscriptionService<TStreamId>(
				new QueuedHandlerThreadPool(bus, "test",
					new QueueStatsManager(), new QueueTrackers()),
				new FakeReadIndex<TLogFormat, TStreamId>(_ => false, new MetaStreamLookup()),
				new IODispatcher(bus, bus), bus,
				new PersistentSubscriptionConsumerStrategyRegistry(bus, bus,
					Array.Empty<IPersistentSubscriptionConsumerStrategyFactory>()), trackers.PersistentSubscriptionTracker);
			_envelope = new CallbackEnvelope(_replySource.SetResult);
			_sut.Start();
		}

		private void Handle(TMessage message) =>
			typeof(PersistentSubscriptionService<TStreamId>).GetMethod("Handle", new[] { typeof(TMessage) })!
				.Invoke(_sut, new object[] { message });

		protected abstract TMessage CreateMessage(IEnvelope envelope);

		protected abstract TResult GetResult(TReply reply);

		[Test]
		public async Task test() {
			Handle(CreateMessage(_envelope));
			var result = await _replySource.Task.WithTimeout();
			Assert.IsInstanceOf<TReply>(result);

			Assert.AreEqual(
				_expectedResult, GetResult((TReply)result));
		}

		private class MetaStreamLookup : IMetastreamLookup<TStreamId> {
			public bool IsMetaStream(TStreamId streamId) => throw new NotSupportedException();

			public TStreamId MetaStreamOf(TStreamId streamId) => throw new NotSupportedException();

			public TStreamId OriginalStreamOf(TStreamId streamId) => throw new NotSupportedException();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class create_stream<TLogFormat, TStreamId> : when_an_error_occurs<TLogFormat, TStreamId,
		ClientMessage.CreatePersistentSubscriptionToStream,
		ClientMessage.CreatePersistentSubscriptionToStreamCompleted,
		ClientMessage.CreatePersistentSubscriptionToStreamCompleted.CreatePersistentSubscriptionToStreamResult> {
		public create_stream() : base(ClientMessage.CreatePersistentSubscriptionToStreamCompleted
			.CreatePersistentSubscriptionToStreamResult.Fail) {
		}

		protected override ClientMessage.CreatePersistentSubscriptionToStream CreateMessage(IEnvelope envelope) =>
			new(
				Guid.NewGuid(), Guid.NewGuid(), envelope, "stream", "group", false, 0L, -1, false, 0, 0, 0, 0, 0, 0,
				0, 0,
				null, ClaimsPrincipal.Current);

		protected override
			ClientMessage.CreatePersistentSubscriptionToStreamCompleted.CreatePersistentSubscriptionToStreamResult
			GetResult(ClientMessage.CreatePersistentSubscriptionToStreamCompleted reply) => reply.Result;
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class create_all<TLogFormat, TStreamId> : when_an_error_occurs<TLogFormat, TStreamId,
		ClientMessage.CreatePersistentSubscriptionToAll,
		ClientMessage.CreatePersistentSubscriptionToAllCompleted,
		ClientMessage.CreatePersistentSubscriptionToAllCompleted.CreatePersistentSubscriptionToAllResult> {
		public create_all() : base(ClientMessage.CreatePersistentSubscriptionToAllCompleted
			.CreatePersistentSubscriptionToAllResult.Fail) {
		}

		protected override ClientMessage.CreatePersistentSubscriptionToAll CreateMessage(IEnvelope envelope) => new(
			Guid.NewGuid(), Guid.NewGuid(), envelope, "group", null, false, new(0, 0), -1, false, 0, 0, 0, 0, 0, 0,
			0, 0, null, ClaimsPrincipal.Current);

		protected override
			ClientMessage.CreatePersistentSubscriptionToAllCompleted.CreatePersistentSubscriptionToAllResult
			GetResult(ClientMessage.CreatePersistentSubscriptionToAllCompleted reply) => reply.Result;
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class update_stream<TLogFormat, TStreamId> : when_an_error_occurs<TLogFormat, TStreamId,
		ClientMessage.UpdatePersistentSubscriptionToStream,
		ClientMessage.UpdatePersistentSubscriptionToStreamCompleted,
		ClientMessage.UpdatePersistentSubscriptionToStreamCompleted.UpdatePersistentSubscriptionToStreamResult> {
		public update_stream() : base(ClientMessage.UpdatePersistentSubscriptionToStreamCompleted
			.UpdatePersistentSubscriptionToStreamResult.Fail) {
		}

		protected override ClientMessage.UpdatePersistentSubscriptionToStream CreateMessage(IEnvelope envelope) =>
			new(
				Guid.NewGuid(), Guid.NewGuid(), envelope, "stream", "group", false, 0L, -1, false, 0, 0, 0, 0, 0, 0,
				0, 0,
				null, ClaimsPrincipal.Current);

		protected override
			ClientMessage.UpdatePersistentSubscriptionToStreamCompleted.UpdatePersistentSubscriptionToStreamResult
			GetResult(ClientMessage.UpdatePersistentSubscriptionToStreamCompleted reply) => reply.Result;
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class update_all<TLogFormat, TStreamId> : when_an_error_occurs<TLogFormat, TStreamId,
		ClientMessage.UpdatePersistentSubscriptionToAll,
		ClientMessage.UpdatePersistentSubscriptionToAllCompleted,
		ClientMessage.UpdatePersistentSubscriptionToAllCompleted.UpdatePersistentSubscriptionToAllResult> {
		public update_all() : base(ClientMessage.UpdatePersistentSubscriptionToAllCompleted
			.UpdatePersistentSubscriptionToAllResult.Fail) {
		}

		protected override ClientMessage.UpdatePersistentSubscriptionToAll CreateMessage(IEnvelope envelope) => new(
			Guid.NewGuid(), Guid.NewGuid(), envelope, "group", false, new(0, 0), -1, false, 0, 0, 0, 0, 0, 0,
			0, 0, null, ClaimsPrincipal.Current);

		protected override
			ClientMessage.UpdatePersistentSubscriptionToAllCompleted.UpdatePersistentSubscriptionToAllResult
			GetResult(ClientMessage.UpdatePersistentSubscriptionToAllCompleted reply) => reply.Result;
	}
}
