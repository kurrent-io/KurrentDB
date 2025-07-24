// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.LogCommon;

public class LogRecordVersion {
	public const byte LogRecordV0 = 0;
	public const byte LogRecordV1 = 1;
}

public class PrepareLogRecordVersion {
	public const byte V0 = 0;

	/// <summary>
	/// ExpectedVersion is a long rather than an int.
	/// </summary>
	public const byte V1 = 1;
}
