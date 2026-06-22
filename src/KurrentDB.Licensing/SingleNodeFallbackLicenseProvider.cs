// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reactive.Linq;
using EventStore.Plugins.Licensing;
using Serilog;

namespace KurrentDB.Licensing;

// Grants an all-features license to single-node deployments when no usable license is
// available (no key configured, or the configured key could not be validated).
public class SingleNodeFallbackLicenseProvider : ILicenseProvider {
	private static readonly ILogger Log = Serilog.Log.ForContext<SingleNodeFallbackLicenseProvider>();

	public SingleNodeFallbackLicenseProvider(ILicenseProvider inner, bool isSingleNode) {
		if (!isSingleNode) {
			// transparent: multi-node behaviour (including no-key / invalid-key failures) is unchanged
			Licenses = inner.Licenses;
			return;
		}

		// A valid license from the inner provider always wins; we only fall back when it
		// cannot supply one. Rx terminates the sequence on error, so this falls back once and
		// stays fallen back until restart (consistent with the Inconclusive rescue in KeygenLicenseProvider).
		var licenses = inner.Licenses
			.Catch((Exception ex) => Observable.Return(CreateSingleNodeFallbackLicense(ex)))
			.Replay(1);
		licenses.Connect();
		Licenses = licenses;
	}

	public IObservable<License> Licenses { get; }

	static License CreateSingleNodeFallbackLicense(Exception ex) {
		var notes = "Single-node license granted.";
		if (ex is NoLicenseKeyException) {
			notes += " No license key configured.";
			Log.Information(notes);
		} else {
			notes += " Configured license could not be validated.";
			Log.Warning(ex, notes);
		}

		var summary = new LicenseSummary(
			LicenseId: "Single Node Dynamic License",
			Company: "Kurrent, Inc",
			IsTrial: false,
			ExpiryUnixTimeSeconds: DateTimeOffset.MaxValue.ToUnixTimeSeconds(),
			IsValid: true,
			Notes: notes);

		return summary.CreateLicense("ALL");
	}
}
