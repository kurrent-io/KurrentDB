// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core;

static class AssemblyLoading {
	/// <summary>
	/// Ensures the V1 assembly is loaded before InMemoryBus discovers message types.
	/// InMemoryBus scans AppDomain.CurrentDomain.GetAssemblies() in its static constructor,
	/// so V1 must be loaded before the bus is first accessed. This module initializer runs
	/// when the Management assembly loads (during node startup), which is early enough.
	/// </summary>
	[ModuleInitializer]
	[SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries")]
	internal static void EnsureV1AssemblyLoaded() {
		RuntimeHelpers.RunClassConstructor(typeof(EventReaderSubscriptionMessage).TypeHandle);
	}
}
