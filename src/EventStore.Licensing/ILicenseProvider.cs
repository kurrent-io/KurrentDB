// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Licensing;

namespace EventStore.Licensing;


public class CurrentLicense {
	public License? License { get; set; }
	public Exception? Exception { get; set; }
}

public interface ILicenseProvider {
	// An exception can be returned as well as a non null license, to allow the provider to
	// provide a claimless license instance while still allowing the error
	// that prevented a proper license from being generated to be visible.
	IObservable<License> Licenses { get; }
}
