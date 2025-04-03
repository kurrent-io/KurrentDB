// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogAbstraction.Common;
using EventStore.Core.LogV2;
using EventStore.Core.LogV3;
using EventStore.Core.LogV3.FASTER;
using EventStore.Core.Settings;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.LogRecords;
using LogV3StreamId = System.UInt32;

namespace EventStore.Core.LogAbstraction;

public record LogFormatAbstractorOptions {
	public string IndexDirectory { get; init; }
	public bool InMemory { get; init; }
	public int InitialReaderCount { get; init; } = ESConsts.PTableInitialReaderCount;
	public int MaxReaderCount { get; init; } = 100;
	public long StreamExistenceFilterSize { get; init; }
	public ICheckpoint StreamExistenceFilterCheckpoint { get; init; }
	public TimeSpan StreamExistenceFilterCheckpointInterval { get; init; } = TimeSpan.FromSeconds(30);
	public TimeSpan StreamExistenceFilterCheckpointDelay { get; init; } = TimeSpan.FromSeconds(5);
	public Func<TFReaderLease> TFReaderLeaseFactory { get; init; }
	public IHasher<string> LowHasher { get; init; } = new XXHashUnsafe();
	public IHasher<string> HighHasher { get; init; } = new Murmur3AUnsafe();
}

public interface ILogFormatAbstractorFactory<TStreamId> {
	LogFormatAbstractor<TStreamId> Create(LogFormatAbstractorOptions options);
}

public class LogV2FormatAbstractorFactory : ILogFormatAbstractorFactory<string> {
	public LogV2FormatAbstractorFactory() {
	}

	public LogFormatAbstractor<string> Create(LogFormatAbstractorOptions options) {
		var streamExistenceFilter = GenStreamExistenceFilter(options);
		var streamNameIndex = new LogV2StreamNameIndex(streamExistenceFilter);
		var eventTypeIndex = new LogV2EventTypeIndex();

		return new LogFormatAbstractor<string>(
			lowHasher: options.LowHasher,
			highHasher: options.HighHasher,
			streamNameIndex: streamNameIndex,
			streamNameIndexConfirmer: streamNameIndex,
			eventTypeIndex: eventTypeIndex,
			eventTypeIndexConfirmer: eventTypeIndex,
			streamIds: streamNameIndex,
			metastreams: new LogV2SystemStreams(),
			streamNamesProvider: GenStreamNamesProvider(options, streamNameIndex, eventTypeIndex),
			streamIdValidator: new LogV2StreamIdValidator(),
			streamIdConverter: new LogV2StreamIdConverter(),
			emptyStreamId: string.Empty,
			emptyEventTypeId: string.Empty,
			streamIdSizer: new LogV2Sizer(),
			streamExistenceFilter: streamExistenceFilter,
			streamExistenceFilterReader: streamExistenceFilter,
			recordFactory: new LogV2RecordFactory(),
			supportsExplicitTransactions: true,
			partitionManagerFactory: (r, w) => new LogV2PartitionManager());
	}

	private static INameExistenceFilter GenStreamExistenceFilter(
		LogFormatAbstractorOptions options) {

		if (options.InMemory || options.StreamExistenceFilterSize == 0) {
			return new NoNameExistenceFilter();
		}

		var nameExistenceFilter = new StreamExistenceFilter(
			directory: $"{options.IndexDirectory}/{ESConsts.StreamExistenceFilterDirectoryName}",
			filterName: "streamExistenceFilter",
			size: options.StreamExistenceFilterSize,
			checkpoint: options.StreamExistenceFilterCheckpoint,
			checkpointInterval: options.StreamExistenceFilterCheckpointInterval,
			checkpointDelay: options.StreamExistenceFilterCheckpointDelay,
			hasher: new CompositeHasher<string>(
				options.LowHasher,
				options.HighHasher));

		return nameExistenceFilter;
	}

	static IStreamNamesProvider<string> GenStreamNamesProvider(
		LogFormatAbstractorOptions options,
		LogV2StreamNameIndex streamNameIndex,
		LogV2EventTypeIndex eventTypeIndex) =>

		new AdHocStreamNamesProvider<string>(setTableIndex: (self, tableIndex) => {
			self.SystemStreams = new LogV2SystemStreams();
			self.StreamNames = streamNameIndex;
			self.EventTypes = eventTypeIndex;
			self.StreamExistenceFilterInitializer = new LogV2StreamExistenceFilterInitializer(
				options.TFReaderLeaseFactory,
				tableIndex);
		});
}

public class LogV3FormatAbstractorFactory : ILogFormatAbstractorFactory<LogV3StreamId> {
	public LogV3FormatAbstractorFactory() {
	}

	public LogFormatAbstractor<LogV3StreamId> Create(LogFormatAbstractorOptions options) {
		var metastreams = new LogV3Metastreams();
		var recordFactory = new LogV3RecordFactory();
		
		var streamNameIndexPersistence = GenStreamNameIndexPersistence(options);
		var streamExistenceFilter = GenStreamExistenceFilter(options);
		var streamNameIndex = GenStreamNameIndex(streamExistenceFilter, streamNameIndexPersistence, metastreams);

		var streamIds = streamNameIndexPersistence
			.Wrap(x => new NameExistenceFilterValueLookupDecorator<LogV3StreamId>(x, streamExistenceFilter))
			.Wrap(x => new StreamIdLookupMetastreamDecorator(x, metastreams));

		var eventTypeIndexPersistence = GenEventTypeIndexPersistence(options);
		var eventTypeIndex = GenEventTypeIndex(eventTypeIndexPersistence);
		
		var abstractor = new LogFormatAbstractor<LogV3StreamId>(
			lowHasher: new IdentityLowHasher(),
			highHasher: new IdentityHighHasher(),
			streamNameIndex: new StreamNameIndexMetastreamDecorator(streamNameIndex, metastreams),
			streamNameIndexConfirmer: streamNameIndex,
			eventTypeIndex: new EventTypeIndexSystemTypesDecorator(eventTypeIndex),
			eventTypeIndexConfirmer: eventTypeIndex,
			streamIds: streamIds,
			metastreams: metastreams,
			streamNamesProvider: GenStreamNamesProvider(metastreams),
			streamIdValidator: new LogV3StreamIdValidator(),
			streamIdConverter: new LogV3StreamIdConverter(),
			emptyStreamId: 0,
			emptyEventTypeId: 0,
			streamIdSizer: new LogV3Sizer(),
			streamExistenceFilter: streamExistenceFilter,
			// this is for the indexreader to check the existence of the stream using a streamId
			// its important for LogV2 (because it has no stream name index)
			// but not applicable to LogV3 (because you need a streamName not id to check the filter)
			streamExistenceFilterReader: new NoExistenceFilterReader(),
			recordFactory: recordFactory,
			supportsExplicitTransactions: false,
			partitionManagerFactory: (r, w) => new PartitionManager(r, w, recordFactory));
		return abstractor;
	}

	static IStreamNamesProvider<LogV3StreamId> GenStreamNamesProvider(IMetastreamLookup<LogV3StreamId> metastreams) {
		return new AdHocStreamNamesProvider<LogV3StreamId>((self, indexReader) => {
			INameLookup<LogV3StreamId> streamNames = new StreamIdToNameFromStandardIndex(indexReader);
			INameLookup<LogV3StreamId> eventTypes = new EventTypeIdToNameFromStandardIndex(indexReader);
			self.SystemStreams = new LogV3SystemStreams(metastreams, streamNames);
			self.StreamNames = new StreamNameLookupMetastreamDecorator(streamNames, metastreams);
			self.EventTypes = new EventTypeLookupSystemTypesDecorator(eventTypes);
			self.StreamExistenceFilterInitializer = new LogV3StreamExistenceFilterInitializer(streamNames);
		});
	}

	static NameIndex GenStreamNameIndex(
		INameExistenceFilter existenceFilter,
		INameIndexPersistence<LogV3StreamId> persistence,
		IMetastreamLookup<LogV3StreamId> metastreams) =>
		new NameIndex(
			indexName: "StreamNameIndex",
			firstValue: LogV3SystemStreams.FirstRealStream,
			valueInterval: LogV3SystemStreams.StreamInterval,
			existenceFilter: existenceFilter,
			persistence: persistence,
			metastreams: metastreams,
			recordTypeToHandle: typeof(LogV3StreamRecord));

	static INameIndexPersistence<LogV3StreamId> GenStreamNameIndexPersistence(LogFormatAbstractorOptions options) {
		if (options.InMemory) {
			return new NameIndexInMemoryPersistence();
		}

		var persistence = new FASTERNameIndexPersistence(
			indexName: "StreamNameIndexPersistence",
			logDir: $"{options.IndexDirectory}/stream-name-index",
			firstValue: LogV3SystemStreams.FirstRealStream,
			valueInterval: LogV3SystemStreams.StreamInterval,
			initialReaderCount: options.InitialReaderCount,
			maxReaderCount: options.MaxReaderCount,
			enableReadCache: true,
			checkpointInterval: TimeSpan.FromSeconds(60));
		return persistence;
	}

	public static INameExistenceFilter GenStreamExistenceFilter(LogFormatAbstractorOptions options) {
		if (options.InMemory || options.StreamExistenceFilterSize == 0) {
			return new NoNameExistenceFilter();
		}

		var nameExistenceFilter = new StreamExistenceFilter(
			directory: $"{options.IndexDirectory}/{ESConsts.StreamExistenceFilterDirectoryName}",
			filterName: "streamExistenceFilter",
			size: options.StreamExistenceFilterSize,
			checkpoint: options.StreamExistenceFilterCheckpoint,
			checkpointInterval: options.StreamExistenceFilterCheckpointInterval,
			checkpointDelay: options.StreamExistenceFilterCheckpointDelay,
			hasher: null)
		.Wrap(x => new StreamExistenceFilterValidator(x));

		return nameExistenceFilter;
	}

	static INameIndexPersistence<LogV3StreamId> GenEventTypeIndexPersistence(LogFormatAbstractorOptions options) {
		if (options.InMemory) {
			return new NameIndexInMemoryPersistence();
		}

		var persistence = new FASTERNameIndexPersistence(
			indexName: "EventTypeIndexPersistence",
			logDir: $"{options.IndexDirectory}/event-type-index",
			firstValue: LogV3SystemEventTypes.FirstRealEventTypeNumber,
			valueInterval: LogV3SystemEventTypes.EventTypeInterval,
			initialReaderCount: options.InitialReaderCount,
			maxReaderCount: options.MaxReaderCount,
			enableReadCache: true,
			checkpointInterval: TimeSpan.FromSeconds(60));
		return persistence;
	}

	static NameIndex GenEventTypeIndex(INameIndexPersistence<LogV3StreamId> persistence) =>
		new NameIndex(
			indexName: "EventTypeIndex",
			firstValue: LogV3SystemEventTypes.FirstRealEventTypeNumber,
			valueInterval: LogV3SystemEventTypes.EventTypeInterval,
			existenceFilter: new NoNameExistenceFilter(),
			persistence: persistence,
			metastreams: null,
			recordTypeToHandle: typeof(LogV3EventTypeRecord));
}

public class LogFormatAbstractor<TStreamId> : IDisposable {
	private readonly Func<ITransactionFileReader,ITransactionFileWriter,IPartitionManager> _partitionManagerFactory;

	public LogFormatAbstractor(
		IHasher<TStreamId> lowHasher,
		IHasher<TStreamId> highHasher,
		INameIndex<TStreamId> streamNameIndex,
		INameIndexConfirmer<TStreamId> streamNameIndexConfirmer,
		INameIndex<TStreamId> eventTypeIndex,
		INameIndexConfirmer<TStreamId> eventTypeIndexConfirmer,
		IValueLookup<TStreamId> streamIds,
		IMetastreamLookup<TStreamId> metastreams,
		IStreamNamesProvider<TStreamId> streamNamesProvider,
		IValidator<TStreamId> streamIdValidator,
		IStreamIdConverter<TStreamId> streamIdConverter,
		TStreamId emptyStreamId,
		TStreamId emptyEventTypeId,
		ISizer<TStreamId> streamIdSizer,
		INameExistenceFilter streamExistenceFilter,
		IExistenceFilterReader<TStreamId> streamExistenceFilterReader,
		IRecordFactory<TStreamId> recordFactory,
		bool supportsExplicitTransactions,
		Func<ITransactionFileReader,ITransactionFileWriter,IPartitionManager> partitionManagerFactory) {
		
		_partitionManagerFactory = partitionManagerFactory;

		LowHasher = lowHasher;
		HighHasher = highHasher;
		StreamNameIndex = streamNameIndex;
		StreamNameIndexConfirmer = streamNameIndexConfirmer;
		EventTypeIndex = eventTypeIndex;
		EventTypeIndexConfirmer = eventTypeIndexConfirmer;
		StreamIds = streamIds;
		Metastreams = metastreams;
		StreamNamesProvider = streamNamesProvider;
		StreamIdValidator = streamIdValidator;
		StreamIdConverter = streamIdConverter;
		EmptyStreamId = emptyStreamId;
		EmptyEventTypeId = emptyEventTypeId;
		StreamIdSizer = streamIdSizer;
		StreamExistenceFilter = streamExistenceFilter;
		StreamExistenceFilterReader = streamExistenceFilterReader;
		RecordFactory = recordFactory;
		SupportsExplicitTransactions = supportsExplicitTransactions;
	}

	public void Dispose() {
		StreamNameIndexConfirmer?.Dispose();
		EventTypeIndexConfirmer?.Dispose();
		StreamExistenceFilter?.Dispose();
	}

	public IHasher<TStreamId> LowHasher { get; }
	public IHasher<TStreamId> HighHasher { get; }
	public INameIndex<TStreamId> StreamNameIndex { get; }
	public INameIndexConfirmer<TStreamId> StreamNameIndexConfirmer { get; }
	public INameIndex<TStreamId> EventTypeIndex { get; }
	public INameIndexConfirmer<TStreamId> EventTypeIndexConfirmer { get; }
	public IValueLookup<TStreamId> StreamIds { get; }
	public IMetastreamLookup<TStreamId> Metastreams { get; }
	public IStreamNamesProvider<TStreamId> StreamNamesProvider { get; }
	public IValidator<TStreamId> StreamIdValidator { get; }
	public IStreamIdConverter<TStreamId> StreamIdConverter { get; }
	public TStreamId EmptyStreamId { get; }
	public TStreamId EmptyEventTypeId { get; }
	public ISizer<TStreamId> StreamIdSizer { get; }
	public INameExistenceFilter StreamExistenceFilter { get; }
	public IExistenceFilterReader<TStreamId> StreamExistenceFilterReader { get; }
	public IRecordFactory<TStreamId> RecordFactory { get; }

	public INameLookup<TStreamId> StreamNames => StreamNamesProvider.StreamNames;
	public INameLookup<TStreamId> EventTypes => StreamNamesProvider.EventTypes;
	public ISystemStreamLookup<TStreamId> SystemStreams => StreamNamesProvider.SystemStreams;
	public INameExistenceFilterInitializer StreamExistenceFilterInitializer => StreamNamesProvider.StreamExistenceFilterInitializer;
	public bool SupportsExplicitTransactions { get; }
	
	public IPartitionManager CreatePartitionManager(ITransactionFileReader reader, ITransactionFileWriter writer) {
		return _partitionManagerFactory(reader, writer);
	}
}
