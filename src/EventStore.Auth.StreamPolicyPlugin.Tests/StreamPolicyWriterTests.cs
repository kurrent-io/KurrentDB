// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Threading;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using Xunit;

namespace EventStore.Auth.StreamPolicyPlugin.Tests;

public class StreamPolicyWriterTests {
	private readonly SynchronousScheduler _bus = new("testBus");

	private StreamPolicyWriter CreateSut() {
		return new StreamPolicyWriter(
			_bus,
			StreamPolicySelector.PolicyStream,
			StreamPolicySelector.PolicyEventType,
			PolicyTestHelpers.SerializePolicy);
	}

	[Fact]
	public async Task when_writing_the_default_policy() {
		var tcs = new TaskCompletionSource<ClientMessage.WriteEvents>();
		_bus.Subscribe(new AdHocHandler<ClientMessage.WriteEvents>(msg => {
			tcs.TrySetResult(msg);
		}));

		var sut = CreateSut();
		var writeRequest = sut.WriteDefaultPolicy(StreamPolicySelector.DefaultPolicy, CancellationToken.None);
		var writeMessage = await tcs.Task.WithTimeout();

		// The write succeeds
		writeMessage.Envelope.ReplyWith(
			new ClientMessage.WriteEventsCompleted(writeMessage.CorrelationId, 0, 0, 100, 100));

		// It attempts to write an event with a known id to the start of the stream
		Assert.Equal(StreamPolicyWriter.DefaultStreamPolicyEventId, writeMessage.Events[0].EventId.ToString());
		Assert.Equal(ExpectedVersion.NoStream, writeMessage.ExpectedVersion);

		// It does not require leader
		Assert.False(writeMessage.RequireLeader);

		// The request should complete
		await writeRequest.WithTimeout();
	}

	[Theory]
	[InlineData(ClientMessage.NotHandled.Types.NotHandledReason.NotReady)]
	[InlineData(ClientMessage.NotHandled.Types.NotHandledReason.NotLeader)]
	[InlineData(ClientMessage.NotHandled.Types.NotHandledReason.TooBusy)]
	public async Task when_the_default_policy_write_request_is_not_handled_and_can_be_retried(
		ClientMessage.NotHandled.Types.NotHandledReason notHandledReason) {
		var writeMessages = new List<ClientMessage.WriteEvents>();
		var mre = new ManualResetEvent(false);
		_bus.Subscribe(new AdHocHandler<ClientMessage.WriteEvents>(msg => {
			writeMessages.Add(msg);
			mre.Set();
		}));

		var sut = CreateSut();
		var writeRequest = sut.WriteDefaultPolicy(StreamPolicySelector.DefaultPolicy, CancellationToken.None);
		await mre.WaitAsync().AsTask().WithTimeout();
		mre.Reset();

		Assert.Single(writeMessages);
		writeMessages[0].Envelope.ReplyWith(new ClientMessage.NotHandled(writeMessages[0].CorrelationId, notHandledReason, ""));

		await mre.WaitAsync().AsTask().WithTimeout();
		Assert.Equal(2, writeMessages.Count);
		writeMessages[1].Envelope.ReplyWith(CreateWriteEventsSuccessfullyCompleted(writeMessages[1]));

		// The request should now complete
		await writeRequest.WithTimeout();
	}

	[Theory]
	[InlineData(ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly)]
	public async Task when_the_default_policy_write_request_is_not_handled_and_cannot_be_retried(
		ClientMessage.NotHandled.Types.NotHandledReason notHandledReason) {
		var tcs = new TaskCompletionSource<ClientMessage.WriteEvents>();
		_bus.Subscribe(new AdHocHandler<ClientMessage.WriteEvents>(msg => {
			tcs.TrySetResult(msg);
		}));

		var sut = CreateSut();
		var writeRequest = sut.WriteDefaultPolicy(StreamPolicySelector.DefaultPolicy, CancellationToken.None);
		var writeMessage = await tcs.Task.WithTimeout();

		writeMessage.Envelope.ReplyWith(new ClientMessage.NotHandled(writeMessage.CorrelationId, notHandledReason, ""));

		// The request should now complete
		await writeRequest.WithTimeout();
	}

	[Theory]
	[InlineData(OperationResult.CommitTimeout)]
	[InlineData(OperationResult.PrepareTimeout)]
	[InlineData(OperationResult.ForwardTimeout)]
	[InlineData(OperationResult.AccessDenied)]
	[InlineData(OperationResult.InvalidTransaction)]
	public async Task when_the_default_policy_write_request_fails_and_can_be_retried(
		OperationResult operationResult) {
		var writeMessages = new List<ClientMessage.WriteEvents>();
		var mre = new ManualResetEvent(false);
		_bus.Subscribe(new AdHocHandler<ClientMessage.WriteEvents>(msg => {
			writeMessages.Add(msg);
			mre.Set();
		}));

		var sut = CreateSut();
		var writeRequest = sut.WriteDefaultPolicy(StreamPolicySelector.DefaultPolicy, CancellationToken.None);
		await mre.WaitAsync().AsTask().WithTimeout();
		mre.Reset();

		Assert.Single(writeMessages);
		writeMessages[0].Envelope.ReplyWith(
			new ClientMessage.WriteEventsCompleted(writeMessages[0].CorrelationId, operationResult, ""));

		await mre.WaitAsync().AsTask().WithTimeout();
		Assert.Equal(2, writeMessages.Count);
		writeMessages[1].Envelope.ReplyWith(CreateWriteEventsSuccessfullyCompleted(writeMessages[1]));

		// The request should now complete
		await writeRequest.WithTimeout();
	}

	[Theory]
	[InlineData(OperationResult.WrongExpectedVersion)]
	[InlineData(OperationResult.StreamDeleted)]
	public async Task when_the_default_policy_write_request_fails_and_cannot_be_retried(
		OperationResult operationResult) {
		var tcs = new TaskCompletionSource<ClientMessage.WriteEvents>();
		_bus.Subscribe(new AdHocHandler<ClientMessage.WriteEvents>(msg => {
			tcs.TrySetResult(msg);
		}));

		var sut = CreateSut();
		var writeRequest = sut.WriteDefaultPolicy(StreamPolicySelector.DefaultPolicy, CancellationToken.None);
		var writeMessage = await tcs.Task.WithTimeout();

		writeMessage.Envelope.ReplyWith(
			new ClientMessage.WriteEventsCompleted(writeMessage.CorrelationId, operationResult, ""));

		// The request should complete
		await writeRequest.WithTimeout();
	}

	private ClientMessage.WriteEventsCompleted CreateWriteEventsSuccessfullyCompleted(ClientMessage.WriteEvents msg) =>
		new (msg.CorrelationId, 0, 0, 1000L, 1000L);
}
