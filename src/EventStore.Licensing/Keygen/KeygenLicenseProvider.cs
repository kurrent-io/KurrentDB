// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reactive.Linq;
using EventStore.Plugins.Licensing;
using Serilog;
using Serilog.Events;

namespace EventStore.Licensing.Keygen;

// This abstracts Keygen away from the rest of the system
// responsible for translating the keygen output into an ESDB license (or not)
// responsible for the business decisions of under what circumstances we want to grant an
//    ESDB license to the rest of the system.
public class KeygenLicenseProvider : ILicenseProvider {
	private static readonly ILogger Log = Serilog.Log.ForContext<KeygenLicenseProvider>();

	public KeygenLicenseProvider(
		IObservable<LicenseInfo> keygenLicenses) {

		// not sure that another subject is the right answer here
		var licenses = keygenLicenses
			.Select(licenseInfo => licenseInfo switch {
				// we aren't yet sure what the license status is, grant everything so we don't break a production system
				LicenseInfo.Inconclusive inconclusive => CreateLicense(inconclusive),

				// we are sure what the license status is, turn it into a ESDB license or throw
				LicenseInfo.Conclusive conclusion => CreateLicense(conclusion),

				_ => null,
			})
			.Where(x => x is not null)
			.Select(x => x!)
			.Replay(1);
		licenses.Connect();
		Licenses = licenses;
	}

	public IObservable<License> Licenses { get; private set; }

	// For some reason we were not able to detect if the users license is valid or not
	// we play it safe and grant a license that allows access to all features, to avoid
	// technical problems taking down production deployments.
	// The primary means of protection against license tampering is the license agreement
	static License CreateLicense(LicenseInfo.Inconclusive licenseInfo) {
		Log.Warning("License could not be validated. Please contact Kurrent support.");

		var summary = new LicenseSummary(
			LicenseId: "Temporary License",
			Company: "Kurrent, Inc",
			IsTrial: false,
			ExpiryUnixTimeSeconds: DateTimeOffset.MaxValue.ToUnixTimeSeconds(),
			IsValid: false,
			Notes: "License could not be validated. Please contact Kurrent support.");

		return CreateLicense(summary, ["ALL"]);
	}

	static License CreateLicense(LicenseInfo.Conclusive licenseInfo) {
		Log.Write(
			licenseInfo.Warning
				? LogEventLevel.Warning
				: LogEventLevel.Information,
			$"License {{Name}} {licenseInfo.Detail}. " +
			"Valid: {Valid}. Trial: {Trial}. Expiry: {Expiry:O}",
			licenseInfo.Name,
			licenseInfo.Valid, licenseInfo.Trial, licenseInfo.Expiry);

		if (licenseInfo.Expiry < DateTimeOffset.UtcNow)
			Log.Warning($"The license expired at {licenseInfo.Expiry}");

		// whether an expired license is valid or not is up to the policy in keygen
		// so we don't need any logic for it here
		if (!licenseInfo.Valid) {
			Log.Warning("License {Name} is not valid", licenseInfo.Name);
			throw new Exception($"Invalid license: {licenseInfo.Name}. {licenseInfo.Detail}");
		}

		var summary = new LicenseSummary(
			LicenseId: licenseInfo.LicenseId,
			Company: licenseInfo.Name, // todo: name may not necessarily be the company name, depends what we do in the keygen dashboard
			IsTrial: licenseInfo.Trial,
			ExpiryUnixTimeSeconds: (licenseInfo.Expiry ?? DateTimeOffset.MaxValue).ToUnixTimeSeconds(),
			IsValid: licenseInfo.Valid,
			Notes: licenseInfo.Detail);

		return CreateLicense(summary, licenseInfo.Entitlements);
	}

	static License CreateLicense(LicenseSummary summary, string[] entitlements) {
		var claims = entitlements.ToDictionary(
			x => x,
			x => (object)"true");

		summary?.ExportClaims(claims);

		return License.Create(claims: claims);
	}
}
