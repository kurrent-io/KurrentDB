// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Licensing;

// Allows plugins to access the current license, get updates to it, and reject a license
// if it is missing entitlements
public interface ILicenseService {
	// For checking that the license service itself is authentic
	License SelfLicense { get; }

	License? CurrentLicense { get; }

	// The current license and updates to it
	IObservable<License> Licenses { get; }

	void RejectLicense(Exception ex);
}
