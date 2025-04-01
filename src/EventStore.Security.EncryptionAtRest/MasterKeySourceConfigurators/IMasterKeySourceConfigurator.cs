// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Security.EncryptionAtRest.MasterKeySources;

namespace EventStore.Security.EncryptionAtRest.MasterKeySourceConfigurators;

public interface IMasterKeySourceConfigurator<TOptions> {
	public string Name { get; }
	public void Configure(TOptions options, out IMasterKeySource masterKeySource);
}
