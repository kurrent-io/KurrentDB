// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.Helpers;

internal class StreamWriter {
	private readonly IEventStoreConnection _store;
	private readonly string _stream;
	private readonly long _version;

	public StreamWriter(IEventStoreConnection store, string stream, long version) {
		_store = store;
		_stream = stream;
		_version = version;
	}

	public async Task<TailWriter> Append(params EventData[] events) {
		for (var i = 0; i < events.Length; i++) {
			var expVer = _version == ExpectedVersion.Any ? ExpectedVersion.Any : _version + i;
			var nextExpVer = (await _store.AppendToStreamAsync(_stream, expVer, new[] { events[i] })).NextExpectedVersion;
			if (_version != ExpectedVersion.Any)
				Assert.AreEqual(expVer + 1, nextExpVer);
		}

		return new TailWriter(_store, _stream);
	}
}

internal class TailWriter {
	private readonly IEventStoreConnection _store;
	private readonly string _stream;

	public TailWriter(IEventStoreConnection store, string stream) {
		_store = store;
		_stream = stream;
	}

	public async Task<TailWriter> Then(EventData @event, long expectedVersion) {
		await _store.AppendToStreamAsync(_stream, expectedVersion, new[] { @event });
		return this;
	}
}

internal class TransactionalWriter {
	private readonly IEventStoreConnection _store;
	private readonly string _stream;

	public TransactionalWriter(IEventStoreConnection store, string stream) {
		_store = store;
		_stream = stream;
	}

	public async Task<OngoingTransaction> StartTransaction(long expectedVersion) {
		return new OngoingTransaction(await _store.StartTransactionAsync(_stream, expectedVersion));
	}

	public OngoingTransaction ContinueTransaction(long transactionId) {
		return new OngoingTransaction(_store.ContinueTransaction(transactionId));
	}
}

//TODO GFY this should be removed and merged with the public idea of a transaction.
internal class OngoingTransaction {
	private readonly EventStoreTransaction _transaction;

	public long TransactionId => _transaction.TransactionId;

	public OngoingTransaction(EventStoreTransaction transaction) {
		_transaction = transaction;
	}

	public async Task<OngoingTransaction> Write(params EventData[] events) {
		await _transaction.WriteAsync(events);
		return this;
	}

	public Task<WriteResult> Commit() {
		return _transaction.CommitAsync();
	}
}
