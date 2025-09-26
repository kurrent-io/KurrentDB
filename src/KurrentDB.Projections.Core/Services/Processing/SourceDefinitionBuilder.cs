// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Services.Processing;

public sealed class SourceDefinitionBuilder : IQuerySources {
	private readonly QuerySourceOptions _options = new();
	private List<string> _categories;
	private List<string> _streams;
	private bool _allEvents;
	private List<string> _events;

	public SourceDefinitionBuilder() {
		_options.DefinesFold = true;
	}

	public void FromAll() {
		AllStreams = true;
	}

	public void FromCategory(string categoryName) {
		_categories ??= [];
		_categories.Add(categoryName);
	}

	public void FromStream(string streamName) {
		_streams ??= [];
		_streams.Add(streamName);
	}

	public void AllEvents() {
		_allEvents = true;
	}

	public void NotAllEvents() {
		_allEvents = false;
	}

	public void SetIncludeLinks(bool includeLinks = true) {
		_options.IncludeLinks = includeLinks;
	}

	public void IncludeEvent(string eventName) {
		_events ??= [];
		_events.Add(eventName);
	}

	public void SetByStream() {
		ByStreams = true;
	}

	public void SetByCustomPartitions() {
		ByCustomPartitions = true;
	}

	public void SetDefinesStateTransform() {
		_options.DefinesStateTransform = true;
	}

	public void SetOutputState() {
		_options.ProducesResults = true;
	}

	public void NoWhen() {
		_options.DefinesFold = false;
	}

	public void SetDefinesFold() {
		_options.DefinesFold = true;
	}

	public void SetResultStreamNameOption(string resultStreamName) {
		_options.ResultStreamName = string.IsNullOrWhiteSpace(resultStreamName) ? null : resultStreamName;
	}

	public void SetPartitionResultStreamNamePatternOption(string partitionResultStreamNamePattern) {
		_options.PartitionResultStreamNamePattern = string.IsNullOrWhiteSpace(partitionResultStreamNamePattern)
			? null
			: partitionResultStreamNamePattern;
	}

	public void SetReorderEvents(bool reorderEvents) {
		_options.ReorderEvents = reorderEvents;
	}

	public void SetProcessingLag(int processingLag) {
		_options.ProcessingLag = processingLag;
	}

	public void SetIsBiState(bool isBiState) {
		_options.IsBiState = isBiState;
	}

	public void SetHandlesStreamDeletedNotifications(bool value = true) {
		_options.HandlesDeletedNotifications = value;
	}

	public bool AllStreams { get; private set; }

	public string[] Categories => _categories?.ToArray();

	public string[] Streams => _streams?.ToArray();

	bool IQuerySources.AllEvents => _allEvents;

	public string[] Events => _events?.ToArray();

	public bool ByStreams { get; private set; }

	public bool ByCustomPartitions { get; private set; }

	public bool DefinesStateTransform => _options.DefinesStateTransform;

	public bool ProducesResults => _options.ProducesResults;

	public bool DefinesFold => _options.DefinesFold;

	public bool HandlesDeletedNotifications => _options.HandlesDeletedNotifications;

	public bool IncludeLinksOption => _options.IncludeLinks;

	public string ResultStreamNameOption => _options.ResultStreamName;

	public string PartitionResultStreamNamePatternOption => _options.PartitionResultStreamNamePattern;

	public bool ReorderEventsOption => _options.ReorderEvents;

	public int? ProcessingLagOption => _options.ProcessingLag;

	public bool IsBiState => _options.IsBiState;

	public static IQuerySources From(Action<SourceDefinitionBuilder> configure) {
		var b = new SourceDefinitionBuilder();
		configure(b);
		return b.Build();
	}

	public IQuerySources Build() {
		return QuerySourcesDefinition.From(this);
	}
}
