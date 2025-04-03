// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Enumerators;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Enumerators;

[TestFixture]
public partial class EnumeratorTests {
	public record SubscriptionResponse { }
	public record Event(Guid Id, long EventNumber, TFPos? EventPosition) : SubscriptionResponse { }
	public record SubscriptionConfirmation() : SubscriptionResponse { }
	public record CaughtUp : SubscriptionResponse { }
	public record FellBehind : SubscriptionResponse { }
	public record Checkpoint(Position CheckpointPosition) : SubscriptionResponse { }

	public class EnumeratorWrapper : IAsyncDisposable {
		private readonly IAsyncEnumerator<ReadResponse> _enumerator;

		public EnumeratorWrapper(IAsyncEnumerator<ReadResponse> enumerator) {
			_enumerator = enumerator;
		}

		public ValueTask DisposeAsync() => _enumerator.DisposeAsync();

		public async Task<SubscriptionResponse> GetNext() {
			if (!await _enumerator.MoveNextAsync()) {
				throw new Exception("No more items in enumerator");
			}

			var resp = _enumerator.Current;

			return resp switch {
				ReadResponse.EventReceived eventReceived => new Event(eventReceived.Event.Event.EventId, eventReceived.Event.OriginalEventNumber, eventReceived.Event.OriginalPosition),
				ReadResponse.SubscriptionConfirmed => new SubscriptionConfirmation(),
				ReadResponse.SubscriptionCaughtUp => new CaughtUp(),
				ReadResponse.SubscriptionFellBehind => new FellBehind(),
				ReadResponse.CheckpointReceived checkpointReceived => new Checkpoint(new Position(checkpointReceived.CommitPosition, checkpointReceived.PreparePosition)),
				_ => throw new ArgumentOutOfRangeException(nameof(resp), resp, null),
			};
		}
	}
}
