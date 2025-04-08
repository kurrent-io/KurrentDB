// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using DotNext;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.LogV3;

public class EventTypeLookupSystemTypesDecorator : INameLookup<uint> {
	private readonly INameLookup<uint> _wrapped;

	public EventTypeLookupSystemTypesDecorator(INameLookup<uint> wrapped) {
		_wrapped = wrapped;
	}

	public ValueTask<string> LookupName(uint eventTypeId, CancellationToken token) {
		return LogV3SystemEventTypes.TryGetVirtualEventType(eventTypeId, out var name)
			? new(name)
			: _wrapped.LookupName(eventTypeId, token);
	}

	public ValueTask<Optional<uint>> TryGetLastValue(CancellationToken token)
		=> _wrapped.TryGetLastValue(token);
}
