// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing;

namespace KurrentDB.Projections.Core.Tests;

internal static class SystemProjections {
	public static Task Created(ISubscriber bus) {
		var systemProjectionsReady =
			typeof(ProjectionNamesBuilder.StandardProjections).GetFields(
					BindingFlags.Public |
					BindingFlags.Static |
					BindingFlags.FlattenHierarchy)
				.Where(x => x.IsLiteral && !x.IsInitOnly)
				.Select(x => x.GetRawConstantValue().ToString())
				.ToDictionary(x => x, _ => TaskCompletionSourceFactory.CreateDefault<bool>());

		bus.Subscribe(new AdHocHandler<CoreProjectionStatusMessage.Stopped>(m => {
			if (!systemProjectionsReady.TryGetValue(m.Name, out var ready))
				return;
			ready.TrySetResult(true);
		}));

		return Task.WhenAll(systemProjectionsReady.Values.Select(x => x.Task));
	}
}
