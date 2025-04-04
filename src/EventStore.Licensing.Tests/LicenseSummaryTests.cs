// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using Xunit;

namespace EventStore.Licensing.Tests;

public class LicenseSummaryTests {
	[Fact]
	public void can_export_claims() {
		var r = Random.Shared;
		var sut = new LicenseSummary(
			licenseId: "license123",
			company: "company",
			isTrial: r.Next(2) == 0,
			expiry: DateTimeOffset.Now + TimeSpan.FromHours(1),
			isValid: r.Next(2) == 0,
			notes: "some notes");

		var exported = new Dictionary<string, object>();
		sut.ExportClaims(exported);

		Assert.Equal(6, exported.Keys.Count);

		Assert.Equal(sut.LicenseId, exported["licenseId"]);
		Assert.Equal(sut.Company, exported["company"]);
		Assert.Equal(sut.IsTrial, exported["isTrial"]);
		Assert.Equal(sut.ExpiryUnixTimeSeconds, exported["expiryUnixTimeSeconds"]);
		Assert.Equal(sut.IsValid, exported["isValid"]);
		Assert.Equal(sut.Notes, exported["notes"]);
	}

	[Fact]
	public void exposes_properties() {
		var props = LicenseSummary.Properties;

		Assert.Equal(6, props.Count);

		Assert.Contains("licenseId", props);
		Assert.Contains("company", props);
		Assert.Contains("isTrial", props);
		Assert.Contains("expiryUnixTimeSeconds", props);
		Assert.Contains("isValid", props);
		Assert.Contains("notes", props);
	}
}
