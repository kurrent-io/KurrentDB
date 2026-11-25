// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Licensing;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Licensing;

public class MultiNodeClusterLicenseMonitor(bool isSingleNode) {
	public const string FeatureName = "Multi-Node Cluster";
	public const string Entitlement = "MULTI_NODE_CLUSTER";
	public const string AnnouncementUrl =
		"https://kurrentdb.kurrent.io/blog/licensing-in-kurrentdb-v26-2-and-beyond-what-s-free-and-what-s-licensed/";

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
			onLicenseException: ex => licenseService.RejectLicense(new MultiNodeClusterLicenseException(ex)),
			logger: loggerFactory.CreateLogger<MultiNodeClusterLicenseMonitor>());
	}
}

// Point the operator at the announcement of the licensing change: a cluster that ran without a
// license before 26.2 now shuts down, and the generic license messages do not say why
public class MultiNodeClusterLicenseException(Exception inner) : Exception(
	$"{inner.Message} Multi-node clusters require a license key, single-node deployments do not. " +
	$"See {MultiNodeClusterLicenseMonitor.AnnouncementUrl}",
	inner);
