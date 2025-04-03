// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;

namespace EventStore.Projections.Core.Tests;

internal static class SystemProjections {
	public static Task Created(ISubscriber bus) {
		var systemProjectionsReady =
			typeof(ProjectionNamesBuilder.StandardProjections).GetFields(
					System.Reflection.BindingFlags.Public |
					System.Reflection.BindingFlags.Static |
					System.Reflection.BindingFlags.FlattenHierarchy)
				.Where(x => x.IsLiteral && !x.IsInitOnly)
				.Select(x => x.GetRawConstantValue().ToString())
				.ToDictionary(x => x, _ => new TaskCompletionSource<bool>());

		bus.Subscribe(new AdHocHandler<CoreProjectionStatusMessage.Stopped>(m => {
			if (!systemProjectionsReady.TryGetValue(m.Name, out var ready)) return;
			ready.TrySetResult(true);
		}));

		return Task.WhenAll(systemProjectionsReady.Values.Select(x => x.Task));
	}
}
