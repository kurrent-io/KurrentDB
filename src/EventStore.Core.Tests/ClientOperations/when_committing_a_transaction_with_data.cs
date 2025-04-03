// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using EventStore.ClientAPI.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientOperations;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class when_committing_a_transaction_with_data<TLogFormat, TStreamId> : specification_with_request_manager_integration<TLogFormat, TStreamId> {
	readonly string _streamId = $"new_test_stream_{Guid.NewGuid()}";
	readonly Event _evt = new Event(Guid.NewGuid(), "SomethingHappened", true, Helper.UTF8NoBom.GetBytes("{Value:42}"), null);
	long _transactionId;

	protected override IEnumerable<Message> WithInitialMessages() {
		var requestComplete = WaitForNext<StorageMessage.RequestCompleted>();
		yield return new ClientMessage.TransactionStart(Guid.NewGuid(), Guid.NewGuid(), Envelope, true, _streamId, ExpectedVersion.Any, null);
		requestComplete.Wait();
		AssertEx.IsOrBecomesTrue(() => Envelope.Replies.Count > 0);
		var resp = Envelope.Replies[0] as ClientMessage.TransactionStartCompleted;
		_transactionId = resp?.TransactionId ?? 0;
		requestComplete = WaitForNext<StorageMessage.RequestCompleted>();
		yield return new ClientMessage.TransactionWrite(Guid.NewGuid(), Guid.NewGuid(), Envelope, true, _transactionId, new[] { _evt }, null);
		requestComplete.Wait();
		Interlocked.Exchange(ref CompletionMessage, null);
		Interlocked.Exchange(ref CompletionMessageCount, 0);
		Envelope.Replies.Clear();
	}

	protected override Message When() {
		return new ClientMessage.TransactionCommit(InternalCorrId, ClientCorrId, Envelope, true, _transactionId, null);
	}

	[Test]
	public void successful_request_message_is_published() {
		AssertEx.IsOrBecomesTrue(()=> Interlocked.Read(ref CompletionMessageCount) == 1);
		Assert.AreEqual(InternalCorrId, CompletionMessage.CorrelationId);
		Assert.True(CompletionMessage.Success);
	}

	[Test]
	public void the_envelope_is_replied_to_with_success() {
		AssertEx.IsOrBecomesTrue(() => Envelope.Replies.Count > 0);
		Assert.That(Envelope.Replies.ContainsSingle<ClientMessage.TransactionCommitCompleted>(
			x => x.CorrelationId == ClientCorrId && x.Result == OperationResult.Success), () => $"Does not contain single successful TransactionCommitCompleted with id {ClientCorrId}");
	}
}
