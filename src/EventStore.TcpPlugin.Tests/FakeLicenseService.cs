// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Reactive.Subjects;
using EventStore.Plugins.Licensing;

namespace EventStore.TcpPlugin.Tests;

class FakeLicenseService : ILicenseService {
	public FakeLicenseService(string token) {
		SelfLicense = new License(new(token));
		CurrentLicense = SelfLicense; // they wouldn't normally be the same
		Licenses = new BehaviorSubject<License>(CurrentLicense);
	}

	public License SelfLicense { get; }

	public License? CurrentLicense { get; }

	public IObservable<License> Licenses { get; }

	public void RejectLicense(Exception ex) {
	}
}
