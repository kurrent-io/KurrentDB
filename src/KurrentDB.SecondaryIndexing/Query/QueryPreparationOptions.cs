// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;

namespace KurrentDB.SecondaryIndexing.Query;

/// <summary>
/// Represents query preparation options.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct QueryPreparationOptions {
	/// <summary>
	/// Indicates that the prepared query must be signed to protect it from the modification.
	/// </summary>
	public bool UseDigitalSignature { get; init; }
}
