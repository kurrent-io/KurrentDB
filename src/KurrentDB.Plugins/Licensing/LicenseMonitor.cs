// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Logging;

namespace EventStore.Plugins.Licensing;

public static class LicenseMonitor {
	// the license (ESLv2) prevents tampering with the license mechanism. we make the license mechanism
	// robust enough that circumventing it requires intentional tampering.
	public static async Task<IDisposable> MonitorAsync(
		string featureName,
		string[] requiredEntitlements,
		ILicenseService licenseService,
		Action<Exception> onLicenseException,
		ILogger logger,
		string? licensePublicKey = null,
		Action<int>? onCriticalError = null) {

		licensePublicKey ??= LicenseConstants.LicensePublicKey;
		onCriticalError ??= Environment.Exit;

		// authenticate the license service itself so that we can trust it to
		// 1. send us any licences at all
		// 2. respect our decision to reject licences
		var authentic = await licenseService.SelfLicense.TryValidateAsync(licensePublicKey);
		if (!authentic) {
			// this should never happen, but could if we end up with some unknown LicenseService.
			logger.LogCritical("LicenseService could not be authenticated");
			onCriticalError(11);
		}

		// authenticate the licenses that the license service sends us
		return licenseService.Licenses.Subscribe(
			onNext: async license => {
				if (await license.TryValidateAsync(licensePublicKey)) {
					// got an authentic license. check required entitlements
					if (license.HasEntitlement("ALL"))
						return;

					if (!license.HasEntitlements(requiredEntitlements, out var missing)) {
						onLicenseException(new LicenseEntitlementException(featureName, missing));
					}
				} else {
					// this should never happen
					logger.LogCritical("ESDB License was not valid");
					onLicenseException(new LicenseException(featureName, new Exception("ESDB License was not valid")));
					onCriticalError(12);
				}
			},
			onError: ex => {
				onLicenseException(new LicenseException(featureName, ex));
			});
	}
}
