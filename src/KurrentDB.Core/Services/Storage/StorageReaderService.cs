// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.RateLimiting;
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

	public StorageReaderService(
		IPublisher bus,
		ISubscriber subscriber,
		IReadIndex<TStreamId> readIndex,
		ISystemStreamLookup<TStreamId> systemStreams,
		PartitionedRateLimiter<ResourceAndPriority> limiter,
		IReadOnlyCheckpoint writerCheckpoint,
		IVirtualStreamReader inMemReader,

		//qq we ought to be doing something with trackers and maybe queueStatsManager
		QueueStatsManager queueStatsManager,
		QueueTrackers trackers) {

		Ensure.NotNull(subscriber);
		Ensure.NotNull(systemStreams);
		Ensure.NotNull(limiter);
		Ensure.NotNull(writerCheckpoint);

		_bus = Ensure.NotNull(bus);
		_readIndex = Ensure.NotNull(readIndex);

		var readerWorker = new StorageReaderWorker<TStreamId>(bus, readIndex, systemStreams, writerCheckpoint, inMemReader,
			limiter,
			queueId: 123); //qq

		//qq consider whether we want this bus at all any more.
		var storageReaderBus = new InMemoryBus("StorageReaderBus",
			//qq check that slow message watching is working (moved it from queue to bus)
			watchSlowMsg: true,
			slowMsgThreshold: TimeSpan.FromMilliseconds(200));
		storageReaderBus.Subscribe<ClientMessage.ReadEvent>(readerWorker);
		storageReaderBus.Subscribe<ClientMessage.ReadStreamEventsBackward>(readerWorker);
		storageReaderBus.Subscribe<ClientMessage.ReadStreamEventsForward>(readerWorker);
		storageReaderBus.Subscribe<ClientMessage.ReadAllEventsForward>(readerWorker);
		storageReaderBus.Subscribe<ClientMessage.ReadAllEventsBackward>(readerWorker);
		storageReaderBus.Subscribe<ClientMessage.FilteredReadAllEventsForward>(readerWorker);
		storageReaderBus.Subscribe<ClientMessage.FilteredReadAllEventsBackward>(readerWorker);
		storageReaderBus.Subscribe<StorageMessage.BatchLogExpiredMessages>(readerWorker);
		storageReaderBus.Subscribe<StorageMessage.EffectiveStreamAclRequest>(readerWorker);
		storageReaderBus.Subscribe<StorageMessage.StreamIdFromTransactionIdRequest>(readerWorker);

		//qq the workersMultiHandler used to need Start()ing and stopping, does anything still
		//_workersMultiHandler.Start();

		subscriber.Subscribe<Message>(storageReaderBus);
	}

	void IHandle<SystemMessage.SystemInit>.Handle(SystemMessage.SystemInit message) {
		_bus.Publish(new SystemMessage.ServiceInitialized(nameof(StorageReaderService)));
	}

	async ValueTask IAsyncHandle<SystemMessage.BecomeShuttingDown>.HandleAsync(SystemMessage.BecomeShuttingDown message, CancellationToken token) {
		try {
			await Task.Yield(); //qq await _workersMultiHandler.Stop();
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
