// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core;

static class AssemblyLoading {
	/// <summary>
	/// Force the V1 projections assembly to load before InMemoryBus's static
	/// constructor scans AppDomain assemblies for message types. Fires when the
	/// test runner loads this test assembly — the earliest possible point —
	/// guaranteeing V1 message types are discoverable by the bus.
	/// </summary>
	[ModuleInitializer]
	[SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries")]
	internal static void EnsureV1AssemblyLoaded() {
		RuntimeHelpers.RunClassConstructor(typeof(EventReaderSubscriptionMessage).TypeHandle);
	}
}
