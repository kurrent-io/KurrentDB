// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.LogAbstraction.Common;
using KurrentDB.Core.LogV2;
using KurrentDB.Core.Settings;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.LogAbstraction;

public record LogFormatAbstractorOptions {
	public string IndexDirectory { get; init; }
	public bool InMemory { get; init; }
	public int InitialReaderCount { get; init; } = ESConsts.PTableInitialReaderCount;
	public int MaxReaderCount { get; init; } = 100;
	public long StreamExistenceFilterSize { get; init; }
	public ICheckpoint StreamExistenceFilterCheckpoint { get; init; }
	public TimeSpan StreamExistenceFilterCheckpointInterval { get; init; } = TimeSpan.FromSeconds(30);
	public TimeSpan StreamExistenceFilterCheckpointDelay { get; init; } = TimeSpan.FromSeconds(5);
	public ITransactionFileReader TFReader { get; init; }
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
				options.TFReader,
				tableIndex);
		});
}

public class LogFormatAbstractor<TStreamId> : IDisposable {
	private readonly Func<ITransactionFileReader, ITransactionFileWriter, IPartitionManager> _partitionManagerFactory;

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
		Func<ITransactionFileReader, ITransactionFileWriter, IPartitionManager> partitionManagerFactory) {

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
