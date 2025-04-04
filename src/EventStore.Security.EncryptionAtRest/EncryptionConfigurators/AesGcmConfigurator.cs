// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Plugins.Transforms;
using EventStore.Security.EncryptionAtRest.Transforms.AesGcm;
using Serilog;

namespace EventStore.Security.EncryptionAtRest.EncryptionConfigurators;

public class AesGcmConfigurator : IEncryptionConfigurator<EncryptionAtRestOptions.AesGcmOptions> {
	private static readonly ILogger _logger = Log.ForContext<AesGcmConfigurator>();
	public string Name => "AesGcm";

	public void Configure(EncryptionAtRestOptions.AesGcmOptions options, IReadOnlyList<MasterKey> masterKeys, out IDbTransform encryptionTransform) {
		_logger.Information($"Encryption-At-Rest: ({Name}) Using key size: {options.KeySize} bits");

		try {
			encryptionTransform = new AesGcmDbTransform(masterKeys, options.KeySize);
		} catch (Exception exc) {
			_logger.Error(exc, $"Encryption-At-Rest: ({Name}) Error while creating encryption transform.");
			throw;
		}
	}
}
