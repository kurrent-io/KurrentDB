// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Licensing;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Licensing;

public class MultiNodeClusterLicenseMonitor(bool isSingleNode) {
	public const string FeatureName = "Multi-Node Cluster";
	public const string Entitlement = "MULTI_NODE_CLUSTER";

	public void Monitor(ILicenseService licenseService, ILoggerFactory loggerFactory) {
		// A single node deployment does not need a license, but SingleNodeFallbackLicenseProvider only
		// grants one when no license is available — a valid license that happens to lack this
		// entitlement flows through untouched, so exempt single nodes here.
		if (isSingleNode)
			return;

		_ = LicenseMonitor.MonitorAsync(
			featureName: FeatureName,
			requiredEntitlements: [Entitlement],
			licenseService: licenseService,
			onLicenseException: licenseService.RejectLicense,
			logger: loggerFactory.CreateLogger<MultiNodeClusterLicenseMonitor>());
	}
}
