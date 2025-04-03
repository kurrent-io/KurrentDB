// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Plugins;
using EventStore.Plugins.Transforms;
using EventStore.Security.EncryptionAtRest.EncryptionConfigurators;
using EventStore.Security.EncryptionAtRest.MasterKeySourceConfigurators;
using EventStore.Security.EncryptionAtRest.MasterKeySources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using ILogger = Serilog.ILogger;

namespace EventStore.Security.EncryptionAtRest;

public class EncryptionAtRestPlugin() : SubsystemsPlugin(requiredEntitlements: ["ENCRYPTION_AT_REST"]) {
	const string ConfigRoot = "KurrentDB:EncryptionAtRest";

	private static readonly ILogger _logger = Log.ForContext<EncryptionAtRestPlugin>();

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabled = configuration.GetValue($"{ConfigRoot}:Enabled", defaultValue: false);
		return (enabled, $"Set '{ConfigRoot}:Enabled' to 'true' to enable the encryption at rest plugin");
	}

	public override void ConfigureServices(IServiceCollection services, IConfiguration config) {
		var options = config.GetSection(ConfigRoot).Get<EncryptionAtRestOptions>() ?? new();

		if (!TryConfigureMasterKeySource(
			    options: options.MasterKey,
			    out var masterKeySource))
			return;

		var masterKeys = LoadMasterKeys(masterKeySource);

		if (!TryConfigureEncryptionAlgorithms(
			    options: options.Encryption,
			    masterKeys: masterKeys,
			    out var encryptionTransforms))
			return;

		services.Decorate<IReadOnlyList<IDbTransform>>((dbTransforms, _) =>
			Decorate(dbTransforms, encryptionTransforms));
	}

	private static IReadOnlyList<IDbTransform> Decorate(IReadOnlyList<IDbTransform> dbTransforms, List<IDbTransform> encryptionTransforms) {
		var newDbTransforms = dbTransforms.Concat(encryptionTransforms).ToList();
		return newDbTransforms;
	}

	private static bool TryConfigureMasterKeySource(
		EncryptionAtRestOptions.MasterKeyOptions options,
		out IMasterKeySource masterKeySource) {

		if (options.File is { } fileConfiguratorOptions) {
			var configurator = new FileConfigurator();
			_logger.Information("Encryption-At-Rest: Loaded master key source: {masterKeySource}", configurator.Name);
			configurator.Configure(fileConfiguratorOptions, out masterKeySource);
			return true;
		}

		masterKeySource = null;
		LogErrorAndThrow($"Encryption-At-Rest: No supported master key source was specified");
		return false;
	}

	private static IReadOnlyList<MasterKey> LoadMasterKeys(IMasterKeySource masterKeySource) {
		var masterKeys = masterKeySource.LoadMasterKeys();
		if (masterKeys.Count == 0)
			LogErrorAndThrow("No master key loaded");

		if (masterKeys.DistinctBy(x => x.Id).Count() != masterKeys.Count)
			LogErrorAndThrow("All master keys should have a unique numeric ID");

		_logger.Information($"Encryption-At-Rest: Active master key ID: {masterKeys[^1].Id}");
		return masterKeys;
	}

	private static bool TryConfigureEncryptionAlgorithms(
		EncryptionAtRestOptions.EncryptionOptions options,
		IReadOnlyList<MasterKey> masterKeys,
		out List<IDbTransform> encryptionTransforms) {

		encryptionTransforms = [];

		if (options.AesGcm.Enabled) {
			var configurator = new AesGcmConfigurator();
			_logger.Information("Encryption-At-Rest: Loaded encryption algorithm: {encryptionAlgorithm}", configurator.Name);
			configurator.Configure(options.AesGcm, masterKeys, out var encryptionTransform);
			encryptionTransforms.Add(encryptionTransform);
		}

		if (encryptionTransforms.Count == 0) {
			LogErrorAndThrow($"Encryption-At-Rest: No supported encryption algorithm was specified");
		}

		return true;
	}

	private static void LogErrorAndThrow(string error) {
		_logger.Error(error);
		throw new Exception(error);
	}
}
