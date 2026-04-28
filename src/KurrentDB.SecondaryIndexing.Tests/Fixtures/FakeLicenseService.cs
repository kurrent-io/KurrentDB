// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reactive.Subjects;
using EventStore.Plugins.Licensing;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

internal sealed class FakeLicenseService : ILicenseService {
	public FakeLicenseService() {
		var license = License.Create(new Dictionary<string, object> {
			["ARROW_FLIGHT_SQL"] = true,
		});
		SelfLicense = license;
		CurrentLicense = license;
		Licenses = new BehaviorSubject<License>(license);
	}

	public License SelfLicense { get; }
	public License? CurrentLicense { get; }
	public IObservable<License> Licenses { get; }

	public void RejectLicense(Exception ex) => RejectionException = ex;
	public Exception? RejectionException { get; private set; }
}
