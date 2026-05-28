// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;

namespace KurrentDB.SecondaryIndexing.Query;

/// <summary>
/// Represents query execution options.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct QueryExecutionOptions {

	/// <summary>
	/// Checks the digital signature of the prepared query.
	/// </summary>
	/// <seealso cref="QueryPreparationOptions.UseDigitalSignature"/>
	public bool CheckIntegrity { get; init; }
}
