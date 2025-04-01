// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Licensing;
using EventStore.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit;
using System.Reactive.Subjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace EventStore.Auth.StreamPolicyPlugin.Tests;

public class StreamPolicySelectorFactoryTests {
	[Theory]
	[InlineData(true, "STREAM_POLICY_AUTHORIZATION", true)]
	[InlineData(false, "STREAM_POLICY_AUTHORIZATION", false)]
	[InlineData(true, "NONE", false)]
	[InlineData(false, "NONE", false)]
	public void respects_license(bool licensePresent, string entitlement, bool expectedIsLicensed) {
		// given
		using var sut = new StreamPolicySelectorFactory();

		var config = new ConfigurationBuilder().Build();
		var builder = WebApplication.CreateBuilder();
		var licenseService = new FakeLicenseService(licensePresent, entitlement);
		builder.Services.AddSingleton<ILicenseService>(licenseService);

		((IPlugableComponent)sut).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();

		// when
		((IPlugableComponent)sut).ConfigureApplication(app, config);

		// then
		Assert.True(sut.IsLicensed == expectedIsLicensed);
		Assert.Null(licenseService.RejectionException); // plugin can disable but never rejects the license
	}

	class FakeLicenseService : ILicenseService {
		// there is tooling in CommercialHA FakeLicenseService to generate these
		static readonly Dictionary<string, string> HardCodedTokens = new() {
			{ "STREAM_POLICY_AUTHORIZATION", "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOiJlc2RiIiwiaXNzIjoiZXNkYiIsImV4cCI6MTgyNDQ1NjgyOSwianRpIjoiMmIzMjlmOWMtNGFlZC00ZDlkLWJjOTEtYTQ4YWFkZWRiNTE0Iiwic3ViIjoiRVNEQiBUZXN0cyIsIklzVHJpYWwiOiJUcnVlIiwiSXNFeHBpcmVkIjoiRmFsc2UiLCJJc1ZhbGlkIjoiVHJ1ZSIsIklzRmxvYXRpbmciOiJUcnVlIiwiRGF5c1JlbWFpbmluZyI6IjEiLCJTdGFydERhdGUiOiIyNi8wNC8yMDI0IDAwOjAwOjAwICswMTowMCIsIlNUUkVBTV9QT0xJQ1lfQVVUSE9SSVpBVElPTiI6InRydWUiLCJpYXQiOjE3Mjk4NDg4MjksIm5iZiI6MTcyOTg0ODgyOX0.ZigSD46YoSWSiozvGQXJ7FXgtEallqyTSvfcywCO_Z_d8cRlCJRb27bzJ1q4UtrBP99SBbttN2etmzyCZhhASAH55W4oo1Vh9iEbBu3uiH8ZPdBD8_V4v83djh70Fux0JUpQWcFq8bBsscXdg7fE2MpaRU004Ei3C03P3Es8jtsXRJ6rkkFFsa0NIQtPnmj44lZfAnU5A_BgamW6_OoPHjB1vEA9LUquQ5p7ovZxK1cv2lgs0lh3drYFyflqI1sAqJof4HCmMV1oK4U-1j9UhGov6J7TWtvSfbpiRToQayGe8yKmYEqydhQhgFlNBfjvv11Z_1uWyVyItopl7W5o9g" },
			{ "NONE", "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOiJlc2RiIiwiaXNzIjoiZXNkYiIsImV4cCI6MTgyNDQ1NjgyOSwianRpIjoiOTVmZTY2YzAtMDRkMi00MjExLWI1ZGQtNTAyM2MyYTAxMGFiIiwic3ViIjoiRVNEQiBUZXN0cyIsIklzVHJpYWwiOiJUcnVlIiwiSXNFeHBpcmVkIjoiRmFsc2UiLCJJc1ZhbGlkIjoiVHJ1ZSIsIklzRmxvYXRpbmciOiJUcnVlIiwiRGF5c1JlbWFpbmluZyI6IjEiLCJTdGFydERhdGUiOiIyNi8wNC8yMDI0IDAwOjAwOjAwICswMTowMCIsIk5PTkUiOiJ0cnVlIiwiaWF0IjoxNzI5ODQ4ODI5LCJuYmYiOjE3Mjk4NDg4Mjl9.R24i-ZAow3BhRaST3n25Uc_nQ184k83YRZZ0oRcWbU9B9XNLRH0Iegj0HmkyzkT50I4gcIJOIfcO6mIPp4Y959CP7aTAlt7XEnXoGF0GwsfXatAxy4iXG8Gpya7INgMoWEeN0v8eDH8_OVmnieOxeba9ex5j1oAW_FtQDMzcFjAeErpW__8zmkCsn6GzvlhdLE4e3r2wjshvrTTcS_1fvSVjQZov5ce2sVBJPegjCLO_QGiIBK9QTnpHrhe6KCYje6fSTjgty0V1Qj22bftvrXreYzQijPrnC_ek1BwV-A1JvacZugMCPIy8WvE5jE3hVYRWGGUzQZ-CibPGsjudYA" },
		};

		public FakeLicenseService(bool createLicense = true, string? entitlement = null) {
			var license = new License(new JsonWebToken(HardCodedTokens[entitlement ?? "NONE"]));
			SelfLicense = license;

			if (createLicense) {
				CurrentLicense = license;
				Licenses = new BehaviorSubject<License>(license);
			} else {
				var licenses = new Subject<License>();
				licenses.OnError(new Exception("no license"));
				Licenses = licenses;
			}
		}

		public License SelfLicense { get; }

		public License? CurrentLicense { get; }

		public IObservable<License> Licenses { get; }

		public void RejectLicense(Exception ex) {
			RejectionException = ex;
		}

		public Exception? RejectionException { get; private set; }
	}
}

