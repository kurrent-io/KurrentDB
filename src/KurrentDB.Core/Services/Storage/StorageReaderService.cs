// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Messages;
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
	private readonly ThreadPoolMessageScheduler _workersHandler;

	public StorageReaderService(
		IPublisher bus,
		ISubscriber subscriber,
		IReadIndex<TStreamId> readIndex,
		ISystemStreamLookup<TStreamId> systemStreams,
		IReadOnlyCheckpoint writerCheckpoint,
		IVirtualStreamReader inMemReader,
		QueueStatsManager queueStatsManager,
		QueueTrackers trackers) {
		Ensure.NotNull(subscriber);
		Ensure.NotNull(systemStreams);
		Ensure.NotNull(writerCheckpoint);

		_bus = Ensure.NotNull(bus);
		_readIndex = Ensure.NotNull(readIndex);

		var worker = new StorageReaderWorker<TStreamId>(bus, readIndex, systemStreams, writerCheckpoint, inMemReader);
		var storageReaderBus = new InMemoryBus("StorageReaderBus", watchSlowMsg: false);

		storageReaderBus.Subscribe<ClientMessage.ReadEvent>(worker);
		storageReaderBus.Subscribe<ClientMessage.ReadStreamEventsBackward>(worker);
		storageReaderBus.Subscribe<ClientMessage.ReadStreamEventsForward>(worker);
		storageReaderBus.Subscribe<ClientMessage.ReadAllEventsForward>(worker);
		storageReaderBus.Subscribe<ClientMessage.ReadAllEventsBackward>(worker);
		storageReaderBus.Subscribe<ClientMessage.FilteredReadAllEventsForward>(worker);
		storageReaderBus.Subscribe<ClientMessage.FilteredReadAllEventsBackward>(worker);
		storageReaderBus.Subscribe<StorageMessage.BatchLogExpiredMessages>(worker);
		storageReaderBus.Subscribe<StorageMessage.EffectiveStreamAclRequest>(worker);
		storageReaderBus.Subscribe<StorageMessage.StreamIdFromTransactionIdRequest>(worker);

		_workersHandler = new ThreadPoolMessageScheduler(storageReaderBus) {
			SynchronizeMessagesWithUnknownAffinity = false,
			Name = "StorageReaderQueue",
		};
		_workersHandler.Start();

		subscriber.Subscribe<ClientMessage.ReadEvent>(_workersHandler);
		subscriber.Subscribe<ClientMessage.ReadStreamEventsBackward>(_workersHandler);
		subscriber.Subscribe<ClientMessage.ReadStreamEventsForward>(_workersHandler);
		subscriber.Subscribe<ClientMessage.ReadAllEventsForward>(_workersHandler);
		subscriber.Subscribe<ClientMessage.ReadAllEventsBackward>(_workersHandler);
		subscriber.Subscribe<ClientMessage.FilteredReadAllEventsForward>(_workersHandler);
		subscriber.Subscribe<ClientMessage.FilteredReadAllEventsBackward>(_workersHandler);
		subscriber.Subscribe<StorageMessage.BatchLogExpiredMessages>(_workersHandler);
		subscriber.Subscribe<StorageMessage.EffectiveStreamAclRequest>(_workersHandler);
		subscriber.Subscribe<StorageMessage.StreamIdFromTransactionIdRequest>(_workersHandler);
	}

	void IHandle<SystemMessage.SystemInit>.Handle(SystemMessage.SystemInit message) {
		_bus.Publish(new SystemMessage.ServiceInitialized(nameof(StorageReaderService)));
	}

	async ValueTask IAsyncHandle<SystemMessage.BecomeShuttingDown>.HandleAsync(SystemMessage.BecomeShuttingDown message, CancellationToken token) {
		try {
			await _workersHandler.Stop();
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
