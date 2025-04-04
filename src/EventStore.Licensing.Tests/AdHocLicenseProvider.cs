// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Reactive.Subjects;
using EventStore.Plugins.Licensing;

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
