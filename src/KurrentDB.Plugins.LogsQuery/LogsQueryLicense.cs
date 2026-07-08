// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Plugins.LogsQuery;

internal sealed class LogsQueryLicense {
	public const string Entitlement = "LOGS_ENDPOINT";

	private volatile bool _licensed = true;
	public bool IsLicensed => _licensed;

	internal void Disable() => _licensed = false;
}
