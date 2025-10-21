// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Core.TransactionLog;

/// <summary>
/// Represents read cursor.
/// </summary>
public interface IReadCursor {

	/// <summary>
	/// Gets or sets the position within the transaction file.
	/// </summary>
	long Position { get; set; }
}
