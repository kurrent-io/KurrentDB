// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Services.Processing;

public class ProjectionNamesBuilder {
	public static class StandardProjections {
		public const string StreamsStandardProjection = "$streams";
		public const string StreamByCategoryStandardProjection = "$stream_by_category";
		public const string EventByCategoryStandardProjection = "$by_category";
		public const string EventByTypeStandardProjection = "$by_event_type";
		public const string EventByCorrIdStandardProjection = "$by_correlation_id";
	}

	public static ProjectionNamesBuilder CreateForTest(string name) => new(name);

	private readonly string _resultStreamName;
	private readonly string _partitionCatalogStreamName;
	private readonly string _checkpointStreamName;
	private readonly string _orderStreamName;
	private readonly string _emittedStreamsName;
	private readonly string _emittedStreamsCheckpointName;

	private ProjectionNamesBuilder(string name) : this(name, new QuerySourcesDefinition()) {
	}

	public ProjectionNamesBuilder(string name, IQuerySources sources) {
		ArgumentNullException.ThrowIfNull(sources);
		EffectiveProjectionName = name;
		_resultStreamName = sources.ResultStreamNameOption
		                    ?? ProjectionsStreamPrefix + EffectiveProjectionName + ProjectionsStateStreamSuffix;
		_partitionCatalogStreamName = ProjectionsStreamPrefix + EffectiveProjectionName + ProjectionPartitionCatalogStreamSuffix;
		_checkpointStreamName = ProjectionsStreamPrefix + EffectiveProjectionName + ProjectionCheckpointStreamSuffix;
		_orderStreamName = ProjectionsStreamPrefix + EffectiveProjectionName + ProjectionOrderStreamSuffix;
		_emittedStreamsName = ProjectionsStreamPrefix + EffectiveProjectionName + ProjectionEmittedStreamSuffix;
		_emittedStreamsCheckpointName = ProjectionsStreamPrefix + EffectiveProjectionName + ProjectionEmittedStreamSuffix +
		                                ProjectionCheckpointStreamSuffix;
		_partitionResultStreamNamePattern =
			sources.PartitionResultStreamNamePatternOption
			?? $"{ProjectionsStreamPrefix}{EffectiveProjectionName}-{{0}}{ProjectionsStateStreamSuffix}";
	}

	private readonly string _partitionResultStreamNamePattern;

	public string EffectiveProjectionName { get; }

	private string GetPartitionResultStreamName(string partitionName)
		=> string.Format(_partitionResultStreamNamePattern, partitionName);

	public string GetResultStreamName() => _resultStreamName;

	public const string ProjectionsStreamPrefix = "$projections-";
	private const string ProjectionsStateStreamSuffix = "-result";
	private const string ProjectionCheckpointStreamSuffix = "-checkpoint";
	private const string ProjectionEmittedStreamSuffix = "-emittedstreams";
	private const string ProjectionOrderStreamSuffix = "-order";
	private const string ProjectionPartitionCatalogStreamSuffix = "-partitions";
	public const string ProjectionsRegistrationStream = "$projections-$all";

	public string GetPartitionCatalogStreamName() => _partitionCatalogStreamName;

	public string MakePartitionResultStreamName(string statePartition)
		=> string.IsNullOrEmpty(statePartition)
			? GetResultStreamName()
			: GetPartitionResultStreamName(statePartition);

	public string MakePartitionCheckpointStreamName(string statePartition)
		=> !string.IsNullOrEmpty(statePartition)
			? $"{ProjectionsStreamPrefix}{EffectiveProjectionName}-{statePartition}{ProjectionCheckpointStreamSuffix}"
			: throw new InvalidOperationException("Root partition cannot have a partition checkpoint stream");


	public string MakeCheckpointStreamName() => _checkpointStreamName;

	public string GetEmittedStreamsName() => _emittedStreamsName;

	public string GetEmittedStreamsCheckpointName() => _emittedStreamsCheckpointName;

	public string GetOrderStreamName() => _orderStreamName;
}
