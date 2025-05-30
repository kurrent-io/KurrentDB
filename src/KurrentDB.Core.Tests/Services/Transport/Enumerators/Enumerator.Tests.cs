// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Transport.Enumerators;

[TestFixture]
public partial class EnumeratorTests {
	public record SubscriptionResponse { }
	public record Event(Guid Id, long EventNumber, TFPos? EventPosition) : SubscriptionResponse { }
	public record SubscriptionConfirmation() : SubscriptionResponse { }
	public record CaughtUp(ReadResponse.SubscriptionCaughtUp Wrapped) : SubscriptionResponse { }
	public record FellBehind(ReadResponse.SubscriptionFellBehind Wrapped) : SubscriptionResponse { }
	public record Checkpoint(ReadResponse.CheckpointReceived Wrapped, Position CheckpointPosition) : SubscriptionResponse { }

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
				ReadResponse.SubscriptionCaughtUp x => new CaughtUp(x),
				ReadResponse.SubscriptionFellBehind x => new FellBehind(x),
				ReadResponse.CheckpointReceived checkpointReceived => new Checkpoint(checkpointReceived, new Position(checkpointReceived.CommitPosition, checkpointReceived.PreparePosition)),
				_ => throw new ArgumentOutOfRangeException(nameof(resp), resp, null),
			};
		}
	}
}
