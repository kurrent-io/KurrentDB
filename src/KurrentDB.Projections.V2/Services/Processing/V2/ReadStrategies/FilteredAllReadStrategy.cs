// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;

namespace KurrentDB.Projections.Core.Services.Processing.V2.ReadStrategies;

public sealed class FilteredAllReadStrategy(
	IPublisher bus,
	IEventFilter eventFilter,
	ClaimsPrincipal user,
	bool requiresLeader = false)
	: IReadStrategy {

	public async IAsyncEnumerable<ReadResponse> ReadFrom(TFPos checkpoint, [EnumeratorCancellation] CancellationToken ct) {
		Position? position = checkpoint == TFPos.HeadOfTf
			? null
			: Position.FromInt64(checkpoint.CommitPosition, checkpoint.PreparePosition);

		await using var enumerator = new Enumerator.AllSubscriptionFiltered(
			bus: bus,
			expiryStrategy: DefaultExpiryStrategy.Instance,
			checkpoint: position,
			resolveLinks: true,
			eventFilter: eventFilter,
			user: user,
			requiresLeader: requiresLeader,
			maxSearchWindow: null,
			checkpointIntervalMultiplier: 1,
			cancellationToken: ct);

		while (await enumerator.MoveNextAsync()) {
			yield return enumerator.Current;
		}
	}
}
