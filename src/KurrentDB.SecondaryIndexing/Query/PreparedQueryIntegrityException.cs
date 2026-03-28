// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Query;

/// <summary>
/// Indicates that the prepared query is modified.
/// </summary>
public class PreparedQueryIntegrityException : Exception {
	internal PreparedQueryIntegrityException()
		: base("Prepared query is invalid") {
	}
}
