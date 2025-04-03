// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Security.EncryptionAtRest.MasterKeySources;
using EventStore.Security.EncryptionAtRest.MasterKeySources.File;
using Serilog;

namespace EventStore.Security.EncryptionAtRest.MasterKeySourceConfigurators;

public class FileConfigurator : IMasterKeySourceConfigurator<EncryptionAtRestOptions.FileConfiguratorOptions> {
	private static readonly ILogger _logger = Log.ForContext<FileConfigurator>();
	public string Name => "File";

	public void Configure(EncryptionAtRestOptions.FileConfiguratorOptions options, out IMasterKeySource masterKeySource) {
		if (string.IsNullOrWhiteSpace(options.KeyPath))
			LogErrorAndThrow($"Encryption-At-Rest: ({Name}) '{nameof(options.KeyPath)}' not specified.");

		try {
			masterKeySource = new FileSource(options.KeyPath);
		} catch (Exception exc) {
			_logger.Error(exc, $"Encryption-At-Rest: ({Name}) Error while configuring master key source.");
			throw;
		}
	}

	private static void LogErrorAndThrow(string error) {
		_logger.Error(error);
		throw new Exception(error);
	}
}
