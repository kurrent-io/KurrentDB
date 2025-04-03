// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Index;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.LogAbstraction;

// mechanism to delay construction of StreamNames and SystemStreams until the IndexReader is available
public class AdHocStreamNamesProvider<TStreamId> : IStreamNamesProvider<TStreamId> {
	private readonly Action<AdHocStreamNamesProvider<TStreamId>, IIndexReader<TStreamId>> _setReader;
	private readonly Action<AdHocStreamNamesProvider<TStreamId>, ITableIndex> _setTableIndex;

	private ISystemStreamLookup<TStreamId> _systemStreams;
	private INameLookup<TStreamId> _streamNames;
	private INameLookup<TStreamId> _eventTypes;
	private INameExistenceFilterInitializer _streamExistenceFilterInitializer;

	public AdHocStreamNamesProvider(
		Action<AdHocStreamNamesProvider<TStreamId>, IIndexReader<TStreamId>> setReader = null,
		Action<AdHocStreamNamesProvider<TStreamId>, ITableIndex> setTableIndex = null) {

		_setReader = setReader;
		_setTableIndex = setTableIndex;
	}

	public INameLookup<TStreamId> StreamNames {
		get => _streamNames ?? throw new InvalidOperationException("Call SetReader or SetTableIndex first");
		set => _streamNames = value;
	}

	public INameLookup<TStreamId> EventTypes {
		get => _eventTypes ?? throw new InvalidOperationException("Call SetReader or SetTableIndex first");
		set => _eventTypes = value;
	}

	public ISystemStreamLookup<TStreamId> SystemStreams {
		get => _systemStreams ?? throw new InvalidOperationException("Call SetReader or SetTableIndex first");
		set => _systemStreams = value;
	}

	public INameExistenceFilterInitializer StreamExistenceFilterInitializer {
		get => _streamExistenceFilterInitializer ?? throw new InvalidOperationException("Call SetReader or SetTableIndex first");
		set => _streamExistenceFilterInitializer = value;
	}

	public void SetReader(IIndexReader<TStreamId> reader) =>
		_setReader?.Invoke(this, reader);

	public void SetTableIndex(ITableIndex tableIndex) =>
		_setTableIndex?.Invoke(this, tableIndex);
}
