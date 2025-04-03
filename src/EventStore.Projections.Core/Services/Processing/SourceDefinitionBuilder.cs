// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Processing;

public sealed class SourceDefinitionBuilder : IQuerySources
{
	private readonly QuerySourceOptions _options = new QuerySourceOptions();
	private bool _allStreams;
	private List<string> _categories;
	private List<string> _streams;
	private bool _allEvents;
	private List<string> _events;
	private bool _byStream;
	private bool _byCustomPartitions;
	private long? _limitingCommitPosition;

	public SourceDefinitionBuilder() {
		_options.DefinesFold = true;
	}

	public void FromAll() {
		_allStreams = true;
	}

	public void FromCategory(string categoryName) {
		if (_categories == null)
			_categories = new List<string>();
		_categories.Add(categoryName);
	}

	public void FromStream(string streamName) {
		if (_streams == null)
			_streams = new List<string>();
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
		if (_events == null)
			_events = new List<string>();
		_events.Add(eventName);
	}

	public void SetByStream() {
		_byStream = true;
	}

	public void SetByCustomPartitions() {
		_byCustomPartitions = true;
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
		_options.ResultStreamName = String.IsNullOrWhiteSpace(resultStreamName) ? null : resultStreamName;
	}

	public void SetPartitionResultStreamNamePatternOption(string partitionResultStreamNamePattern) {
		_options.PartitionResultStreamNamePattern = String.IsNullOrWhiteSpace(partitionResultStreamNamePattern)
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

	public bool AllStreams {
		get { return _allStreams; }
	}

	public string[] Categories {
		get { return _categories != null ? _categories.ToArray() : null; }
	}

	public string[] Streams {
		get { return _streams != null ? _streams.ToArray() : null; }
	}

	bool IQuerySources.AllEvents {
		get { return _allEvents; }
	}

	public string[] Events {
		get { return _events != null ? _events.ToArray() : null; }
	}

	public bool ByStreams {
		get { return _byStream; }
	}

	public bool ByCustomPartitions {
		get { return _byCustomPartitions; }
	}

	public long? LimitingCommitPosition {
		get { return _limitingCommitPosition; }
	}

	public bool DefinesStateTransform {
		get { return _options.DefinesStateTransform; }
	}

	public bool ProducesResults {
		get { return _options.ProducesResults; }
	}

	public bool DefinesFold {
		get { return _options.DefinesFold; }
	}

	public bool HandlesDeletedNotifications {
		get { return _options.HandlesDeletedNotifications; }
	}

	public bool IncludeLinksOption {
		get { return _options.IncludeLinks; }
	}

	public string ResultStreamNameOption {
		get { return _options.ResultStreamName; }
	}

	public string PartitionResultStreamNamePatternOption {
		get { return _options.PartitionResultStreamNamePattern; }
	}

	public bool ReorderEventsOption {
		get { return _options.ReorderEvents; }
	}

	public int? ProcessingLagOption {
		get { return _options.ProcessingLag; }
	}

	public bool IsBiState {
		get { return _options.IsBiState; }
	}

	public static IQuerySources From(Action<SourceDefinitionBuilder> configure) {
		var b = new SourceDefinitionBuilder();
		configure(b);
		return b.Build();
	}

	public IQuerySources Build() {
		return QuerySourcesDefinition.From(this);
	}

	public void SetLimitingCommitPosition(long limitingCommitPosition) {
		_limitingCommitPosition = limitingCommitPosition;
	}
}
