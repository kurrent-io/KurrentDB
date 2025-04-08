// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using EventStore.Core.LogAbstraction;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.LogV2;

public class LogV2EventTypeIndex :
	INameIndex<string>,
	INameIndexConfirmer<string>,
	IValueLookup<string>,
	INameLookup<string> {

	public void Dispose() {
	}

	public ValueTask InitializeWithConfirmed(INameLookup<string> source, CancellationToken token)
		=> token.IsCancellationRequested ? ValueTask.FromCanceled(token) : ValueTask.CompletedTask;

	public void CancelReservations() {
	}

	public void Confirm(IList<IPrepareLogRecord<string>> prepares, bool catchingUp, IIndexBackend<string> backend) {
	}

	public void Confirm(
		IList<IPrepareLogRecord<string>> prepares,
		CommitLogRecord commit,
		bool catchingUp,
		IIndexBackend<string> backend) {
	}

	public bool GetOrReserve(string eventType, out string eventTypeId, out string createdId, out string createdName) {
		Ensure.NotNull(eventType, "eventType");
		eventTypeId = eventType;
		createdId = default;
		createdName = default;

		return true;
	}

	public string LookupValue(string eventTypeName) => eventTypeName;

	public ValueTask<string> LookupName(string eventTypeId, CancellationToken token)
		=> token.IsCancellationRequested ? ValueTask.FromCanceled<string>(token) : ValueTask.FromResult(eventTypeId);

	public ValueTask<Optional<string>> TryGetLastValue(CancellationToken token)
		=> ValueTask.FromException<Optional<string>>(new NotImplementedException());
}
