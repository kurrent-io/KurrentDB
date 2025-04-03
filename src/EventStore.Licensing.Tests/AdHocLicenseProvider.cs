// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Licensing;
using System;
using System.Reactive.Subjects;

namespace EventStore.Licensing.Tests;

public class AdHocLicenseProvider : ILicenseProvider {
	public AdHocLicenseProvider(License license) {
		LicenseSubject = new BehaviorSubject<License>(license);
	}

	public AdHocLicenseProvider(Exception ex) {
		var licenses = new Subject<License>();
		licenses.OnError(ex);
		LicenseSubject = licenses;
	}

	public IObservable<License> Licenses => LicenseSubject;

	public SubjectBase<License> LicenseSubject { get; private set; }
}
