// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Plugins.Transforms;

namespace EventStore.Security.EncryptionAtRest.EncryptionConfigurators;

public interface IEncryptionConfigurator<TOptions> {
	public string Name { get; }
	public void Configure(
		TOptions options,
		IReadOnlyList<MasterKey> masterKeys,
		out IDbTransform encryptionTransform);
}
