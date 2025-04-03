// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Processing;

public class ProjectionNamesBuilder {
	public static class StandardProjections {
		public const string StreamsStandardProjection = "$streams";
		public const string StreamByCategoryStandardProjection = "$stream_by_category";
		public const string EventByCategoryStandardProjection = "$by_category";
		public const string EventByTypeStandardProjection = "$by_event_type";
		public const string EventByCorrIdStandardProjection = "$by_correlation_id";
	}

	public static ProjectionNamesBuilder CreateForTest(string name) {
		return new ProjectionNamesBuilder(name);
	}

	private readonly string _name;
	private readonly IQuerySources _sources;
	private readonly string _resultStreamName;
	private readonly string _partitionCatalogStreamName;
	private readonly string _checkpointStreamName;
	private readonly string _orderStreamName;
	private readonly string _emittedStreamsName;
	private readonly string _emittedStreamsCheckpointName;

	private ProjectionNamesBuilder(string name)
		: this(name, new QuerySourcesDefinition()) {
	}

	public ProjectionNamesBuilder(string name, IQuerySources sources) {
		if (sources == null) throw new ArgumentNullException("sources");
		_name = name;
		_sources = sources;
		_resultStreamName = _sources.ResultStreamNameOption
		                    ?? ProjectionsStreamPrefix + EffectiveProjectionName + ProjectionsStateStreamSuffix;
		_partitionCatalogStreamName = ProjectionsStreamPrefix + EffectiveProjectionName
		                                                      + ProjectionPartitionCatalogStreamSuffix;
		_checkpointStreamName =
			ProjectionsStreamPrefix + EffectiveProjectionName + ProjectionCheckpointStreamSuffix;
		_orderStreamName = ProjectionsStreamPrefix + EffectiveProjectionName + ProjectionOrderStreamSuffix;
		_emittedStreamsName = ProjectionsStreamPrefix + EffectiveProjectionName + ProjectionEmittedStreamSuffix;
		_emittedStreamsCheckpointName = ProjectionsStreamPrefix + EffectiveProjectionName +
		                                ProjectionEmittedStreamSuffix + ProjectionCheckpointStreamSuffix;
	}

	public string EffectiveProjectionName {
		get { return _name; }
	}


	private string GetPartitionResultStreamName(string partitionName) {
		return String.Format(GetPartitionResultStreamNamePattern(), partitionName);
	}

	public string GetResultStreamName() {
		return _resultStreamName;
	}

	public string GetPartitionResultStreamNamePattern() {
		return _sources.PartitionResultStreamNamePatternOption
		       ?? ProjectionsStreamPrefix + EffectiveProjectionName + "-{0}" + ProjectionsStateStreamSuffix;
	}

	public const string ProjectionsStreamPrefix = "$projections-";
	private const string ProjectionsStateStreamSuffix = "-result";
	private const string ProjectionCheckpointStreamSuffix = "-checkpoint";
	private const string ProjectionEmittedStreamSuffix = "-emittedstreams";
	private const string ProjectionOrderStreamSuffix = "-order";
	private const string ProjectionPartitionCatalogStreamSuffix = "-partitions";
	public const string ProjectionsRegistrationStream = "$projections-$all";

	public string GetPartitionCatalogStreamName() {
		return _partitionCatalogStreamName;
	}

	public string MakePartitionResultStreamName(string statePartition) {
		return String.IsNullOrEmpty(statePartition)
			? GetResultStreamName()
			: GetPartitionResultStreamName(statePartition);
	}

	public string MakePartitionCheckpointStreamName(string statePartition) {
		if (String.IsNullOrEmpty(statePartition))
			throw new InvalidOperationException("Root partition cannot have a partition checkpoint stream");

		return ProjectionsStreamPrefix + EffectiveProjectionName + "-" + statePartition
		       + ProjectionCheckpointStreamSuffix;
	}


	public string MakeCheckpointStreamName() {
		return _checkpointStreamName;
	}

	public string GetEmittedStreamsName() {
		return _emittedStreamsName;
	}

	public string GetEmittedStreamsCheckpointName() {
		return _emittedStreamsCheckpointName;
	}

	public string GetOrderStreamName() {
		return _orderStreamName;
	}
}
