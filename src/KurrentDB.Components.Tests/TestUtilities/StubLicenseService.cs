// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Plugins.Licensing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace KurrentDB.Components.Tests.TestUtilities;

// Test double for ILicenseService. WithLicense (and so LicensedContent / LicensedPageCore) only ever asks
// whether CurrentLicense is non-null, so this just toggles that. The token below is an arbitrary valid JWT
// purely so a License instance can be constructed — its contents are never inspected by the UI gate.
public sealed class StubLicenseService : ILicenseService {
	// Throwaway licence JWT (no entitlements) borrowed from the engine's licensing test fakes.
	const string Token =
		"eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOiJlc2RiIiwiaXNzIjoiZXNkYiIsImV4cCI6MTgyNDQ1NjgyOSwianRpIjoiOTVmZTY2YzAtMDRkMi00MjExLWI1ZGQtNTAyM2MyYTAxMGFiIiwic3ViIjoiRVNEQiBUZXN0cyIsIklzVHJpYWwiOiJUcnVlIiwiSXNFeHBpcmVkIjoiRmFsc2UiLCJJc1ZhbGlkIjoiVHJ1ZSIsIklzRmxvYXRpbmciOiJUcnVlIiwiRGF5c1JlbWFpbmluZyI6IjEiLCJTdGFydERhdGUiOiIyNi8wNC8yMDI0IDAwOjAwOjAwICswMTowMCIsIk5PTkUiOiJ0cnVlIiwiaWF0IjoxNzI5ODQ4ODI5LCJuYmYiOjE3Mjk4NDg4Mjl9.R24i-ZAow3BhRaST3n25Uc_nQ184k83YRZZ0oRcWbU9B9XNLRH0Iegj0HmkyzkT50I4gcIJOIfcO6mIPp4Y959CP7aTAlt7XEnXoGF0GwsfXatAxy4iXG8Gpya7INgMoWEeN0v8eDH8_OVmnieOxeba9ex5j1oAW_FtQDMzcFjAeErpW__8zmkCsn6GzvlhdLE4e3r2wjshvrTTcS_1fvSVjQZov5ce2sVBJPegjCLO_QGiIBK9QTnpHrhe6KCYje6fSTjgty0V1Qj22bftvrXreYzQijPrnC_ek1BwV-A1JvacZugMCPIy8WvE5jE3hVYRWGGUzQZ-CibPGsjudYA";

	public StubLicenseService(bool hasLicense) {
		SelfLicense = new License(new JsonWebToken(Token));
		CurrentLicense = hasLicense ? SelfLicense : null;
	}

	public License SelfLicense { get; }
	public License? CurrentLicense { get; }

	// Never subscribed by the UI gate; a no-op observable keeps us off System.Reactive.
	public IObservable<License> Licenses { get; } = new NoLicenses();

	public void RejectLicense(Exception ex) { }

	sealed class NoLicenses : IObservable<License> {
		public IDisposable Subscribe(IObserver<License> observer) => EmptyDisposable.Instance;
	}

	sealed class EmptyDisposable : IDisposable {
		public static readonly EmptyDisposable Instance = new();
		public void Dispose() { }
	}
}
