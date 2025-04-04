// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Common.Utils;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Services;
using StreamId = System.UInt32;

namespace EventStore.Core.LogV3;

// Decorates a StreamNameIndex, intercepting Metastream (and VirtualStream) calls
public class StreamNameIndexMetastreamDecorator : INameIndex<StreamId> {
	private readonly INameIndex<StreamId> _wrapped;
	private readonly IMetastreamLookup<StreamId> _metastreams;

	public StreamNameIndexMetastreamDecorator(
		INameIndex<StreamId> wrapped,
		IMetastreamLookup<StreamId> metastreams) {

		_wrapped = wrapped;
		_metastreams = metastreams;
	}

	public void CancelReservations() {
		_wrapped.CancelReservations();
	}

	public bool GetOrReserve(string streamName, out StreamId streamId, out StreamId createdId, out string createdName) {
		Ensure.NotNullOrEmpty(streamName, "streamName");
		if (SystemStreams.IsMetastream(streamName)) {
			streamName = SystemStreams.OriginalStreamOf(streamName);
			var ret = GetOrReserve(streamName, out streamId, out createdId, out createdName);
			streamId = _metastreams.MetaStreamOf(streamId);
			return ret;
		}

		if (LogV3SystemStreams.TryGetVirtualStreamId(streamName, out streamId)) {
			createdId = default;
			createdName = default;
			return true;
		}

		return _wrapped.GetOrReserve(streamName, out streamId, out createdId, out createdName);
	}
}
