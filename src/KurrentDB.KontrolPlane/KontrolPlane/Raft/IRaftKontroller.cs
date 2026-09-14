// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.KontrolPlane.Raft;

/// <summary>
/// Reporting that is specific to the Raft-based Kontroller
/// </summary>
public interface IRaftKontroller : IKontroller {
	/// <summary>
	/// Gets this Kontroller's view of the Kontrol Plane cluster, for reporting.
	/// </summary>
	KontrollerClusterInfo GetClusterInfo();
}
