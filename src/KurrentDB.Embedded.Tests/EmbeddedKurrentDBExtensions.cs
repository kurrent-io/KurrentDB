// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Client;

namespace KurrentDB.Embedded.Tests;

public static class EmbeddedKurrentDBExtensions {
	public static KurrentDBClientSettings CreateClientSettings(this EmbeddedKurrentDB self) {
		var settings = KurrentDBClientSettings.Create("kurrentdb://localhost?tls=false");
		settings.CreateHttpMessageHandler = () => UnixDomainSocket.CreateHandler(self.UnixSocketPath);
		return settings;
	}
}
