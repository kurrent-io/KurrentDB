// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;

namespace KurrentDB.Projections.Core.Services.Processing.V2.ReadStrategies;

public sealed class FilteredAllReadStrategy : IReadStrategy {
	private readonly IPublisher _bus;
	private readonly IEventFilter _eventFilter;
	private readonly ClaimsPrincipal _user;
	private readonly bool _requiresLeader;

	private Enumerator.AllSubscriptionFiltered _enumerator;

	public FilteredAllReadStrategy(
		IPublisher bus,
		IEventFilter eventFilter,
		ClaimsPrincipal user,
		bool requiresLeader = false) {
		_bus = bus;
		_eventFilter = eventFilter;
		_user = user;
		_requiresLeader = requiresLeader;
	}

	public async IAsyncEnumerable<ReadResponse> ReadFrom(
		TFPos checkpoint,
		[EnumeratorCancellation] CancellationToken ct) {

		Position? position = checkpoint == TFPos.HeadOfTf
			? null
			: Position.FromInt64(checkpoint.CommitPosition, checkpoint.PreparePosition);

		_enumerator = new Enumerator.AllSubscriptionFiltered(
			bus: _bus,
			expiryStrategy: DefaultExpiryStrategy.Instance,
			checkpoint: position,
			resolveLinks: true,
			eventFilter: _eventFilter,
			user: _user,
			requiresLeader: _requiresLeader,
			maxSearchWindow: null,
			checkpointIntervalMultiplier: 1,
			cancellationToken: ct);

		while (await _enumerator.MoveNextAsync()) {
			yield return _enumerator.Current;
		}
	}

	public async ValueTask DisposeAsync() {
		if (_enumerator is not null) {
			await _enumerator.DisposeAsync();
		}
	}
}
