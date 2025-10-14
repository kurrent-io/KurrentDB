// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Connectors.Tests;

public partial class ConnectorsAssemblyFixture {
	public string NewConnectorId() => $"connector-id-{GenerateShortId()}".ToLowerInvariant();
	public string NewConnectorName() => $"connector-name-{GenerateShortId()}".ToLowerInvariant();
}
