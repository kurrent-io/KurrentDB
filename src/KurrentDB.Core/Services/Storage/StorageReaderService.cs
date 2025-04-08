// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Messages;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.Checkpoint;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Services.Storage;

public abstract class StorageReaderService {
	protected static readonly ILogger Log = Serilog.Log.ForContext<StorageReaderService>();
}

public class StorageReaderService<TStreamId> : StorageReaderService, IHandle<SystemMessage.SystemInit>,
	IAsyncHandle<SystemMessage.BecomeShuttingDown>,
	IHandle<SystemMessage.BecomeShutdown>,
	IHandle<MonitoringMessage.InternalStatsRequest> {

	private readonly IPublisher _bus;
	private readonly IReadIndex _readIndex;
	private readonly MultiQueuedHandler _workersMultiHandler;

	public StorageReaderService(
		IPublisher bus,
		ISubscriber subscriber,
		IReadIndex<TStreamId> readIndex,
		ISystemStreamLookup<TStreamId> systemStreams,
		int threadCount,
		IReadOnlyCheckpoint writerCheckpoint,
		IInMemoryStreamReader inMemReader,
		QueueStatsManager queueStatsManager,
		QueueTrackers trackers) {
		Ensure.NotNull(subscriber);
		Ensure.NotNull(systemStreams);
		Ensure.Positive(threadCount);
		Ensure.NotNull(writerCheckpoint);

		_bus = Ensure.NotNull(bus);
		_readIndex = Ensure.NotNull(readIndex);
		StorageReaderWorker<TStreamId>[] readerWorkers = new StorageReaderWorker<TStreamId>[threadCount];
		InMemoryBus[] storageReaderBuses = new InMemoryBus[threadCount];
		for (var i = 0; i < threadCount; i++) {
			readerWorkers[i] = new(bus, readIndex, systemStreams, writerCheckpoint, inMemReader, i);
			storageReaderBuses[i] = new("StorageReaderBus", watchSlowMsg: false);
			storageReaderBuses[i].Subscribe<ClientMessage.ReadEvent>(readerWorkers[i]);
			storageReaderBuses[i].Subscribe<ClientMessage.ReadStreamEventsBackward>(readerWorkers[i]);
			storageReaderBuses[i].Subscribe<ClientMessage.ReadStreamEventsForward>(readerWorkers[i]);
			storageReaderBuses[i].Subscribe<ClientMessage.ReadAllEventsForward>(readerWorkers[i]);
			storageReaderBuses[i].Subscribe<ClientMessage.ReadAllEventsBackward>(readerWorkers[i]);
			storageReaderBuses[i].Subscribe<ClientMessage.FilteredReadAllEventsForward>(readerWorkers[i]);
			storageReaderBuses[i].Subscribe<ClientMessage.FilteredReadAllEventsBackward>(readerWorkers[i]);
			storageReaderBuses[i].Subscribe<StorageMessage.BatchLogExpiredMessages>(readerWorkers[i]);
			storageReaderBuses[i].Subscribe<StorageMessage.EffectiveStreamAclRequest>(readerWorkers[i]);
			storageReaderBuses[i].Subscribe<StorageMessage.StreamIdFromTransactionIdRequest>(readerWorkers[i]);
		}

		_workersMultiHandler = new MultiQueuedHandler(
			threadCount,
			queueNum => new QueuedHandlerThreadPool(storageReaderBuses[queueNum],
				$"StorageReaderQueue #{queueNum + 1}",
				queueStatsManager,
				trackers,
				groupName: "StorageReaderQueue",
				watchSlowMsg: true,
				slowMsgThreshold: TimeSpan.FromMilliseconds(200)));
		_workersMultiHandler.Start();

		subscriber.Subscribe<ClientMessage.ReadEvent>(_workersMultiHandler);
		subscriber.Subscribe<ClientMessage.ReadStreamEventsBackward>(_workersMultiHandler);
		subscriber.Subscribe<ClientMessage.ReadStreamEventsForward>(_workersMultiHandler);
		subscriber.Subscribe<ClientMessage.ReadAllEventsForward>(_workersMultiHandler);
		subscriber.Subscribe<ClientMessage.ReadAllEventsBackward>(_workersMultiHandler);
		subscriber.Subscribe<ClientMessage.FilteredReadAllEventsForward>(_workersMultiHandler);
		subscriber.Subscribe<ClientMessage.FilteredReadAllEventsBackward>(_workersMultiHandler);
		subscriber.Subscribe<StorageMessage.BatchLogExpiredMessages>(_workersMultiHandler);
		subscriber.Subscribe<StorageMessage.EffectiveStreamAclRequest>(_workersMultiHandler);
		subscriber.Subscribe<StorageMessage.StreamIdFromTransactionIdRequest>(_workersMultiHandler);
	}

	void IHandle<SystemMessage.SystemInit>.Handle(SystemMessage.SystemInit message) {
		_bus.Publish(new SystemMessage.ServiceInitialized(nameof(StorageReaderService)));
	}

	async ValueTask IAsyncHandle<SystemMessage.BecomeShuttingDown>.HandleAsync(SystemMessage.BecomeShuttingDown message, CancellationToken token) {
		try {
			await _workersMultiHandler.Stop();
		} catch (Exception exc) {
			Log.Error(exc, "Error while stopping readers multi handler.");
		}

		_bus.Publish(new SystemMessage.ServiceShutdown(nameof(StorageReaderService)));
	}

	void IHandle<SystemMessage.BecomeShutdown>.Handle(SystemMessage.BecomeShutdown message) {
		// by now (in case of successful shutdown process), all readers and writers should not be using ReadIndex
		_readIndex.Close();
	}

	void IHandle<MonitoringMessage.InternalStatsRequest>.Handle(MonitoringMessage.InternalStatsRequest message) {
		var s = _readIndex.GetStatistics();
		var stats = new Dictionary<string, object> {
			{ "es-readIndex-cachedRecord", s.CachedRecordReads },
			{ "es-readIndex-notCachedRecord", s.NotCachedRecordReads },
			{ "es-readIndex-cachedStreamInfo", s.CachedStreamInfoReads },
			{ "es-readIndex-notCachedStreamInfo", s.NotCachedStreamInfoReads },
			{ "es-readIndex-cachedTransInfo", s.CachedTransInfoReads },
			{ "es-readIndex-notCachedTransInfo", s.NotCachedTransInfoReads },
		};

		message.Envelope.ReplyWith(new MonitoringMessage.InternalStatsRequestResponse(stats));
	}
}
