// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Reactive.Subjects;
using EventStore.Plugins.Licensing;
using KurrentDB.Licensing;

namespace KurrentDB.Core.Tests.Helpers;

public class TestLicenseProvider : ILicenseProvider {
	public TestLicenseProvider(params string[] entitlements) {
		var summary = new LicenseSummary(
			LicenseId: "Test License",
			Company: "Kurrent, Inc",
			IsTrial: false,
			ExpiryUnixTimeSeconds: DateTimeOffset.MaxValue.ToUnixTimeSeconds(),
			IsValid: true,
			Notes: "Test License");

		Licenses = new BehaviorSubject<License>(summary.CreateLicense(entitlements));
	}

	public IObservable<License> Licenses { get; }
}
