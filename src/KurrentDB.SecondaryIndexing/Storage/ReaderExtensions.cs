// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.SecondaryIndexing.Storage;

public static class ReaderExtensions {
	public static IEnumerable<ResolvedEvent> ReadEvents(this IPublisher publisher, long[] logPositions) {
		using var enumerator = GetEnumerator();

		while (enumerator.MoveNext()) {
			if (enumerator.Current is ReadResponse.EventReceived eventReceived) {
				yield return eventReceived.Event;
			}
		}

		yield break;

		IEnumerator<ReadResponse> GetEnumerator() {
			return new Enumerator.ReadLogEventsSync(
				bus: publisher,
				logPositions: logPositions,
				user: SystemAccounts.System,
				deadline: DefaultDeadline
			);
		}
	}

	static readonly DateTime DefaultDeadline = DateTime.UtcNow.AddYears(1);
}
