// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using EventStore.Plugins.Licensing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace EventStore.Core.XUnit.Tests.Services.Archive;

public class FakeLicenseService : ILicenseService {
	static readonly Dictionary<string, string> _hardCodedTokens = new() {
		{ "ARCHIVE", "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOiJlc2RiIiwiaXNzIjoiZXNkYiIsImV4cCI6MTgyNjAwODExMiwianRpIjoiYWFjNDJiZjctMmU3Mi00ZjkxLWE5Y2EtNTcyNDVhOTYxMmRjIiwic3ViIjoiRVNEQiBUZXN0cyIsIklzVHJpYWwiOiJUcnVlIiwiSXNFeHBpcmVkIjoiRmFsc2UiLCJJc1ZhbGlkIjoiVHJ1ZSIsIklzRmxvYXRpbmciOiJUcnVlIiwiRGF5c1JlbWFpbmluZyI6IjEiLCJTdGFydERhdGUiOiI0LzI2LzIwMjQgMTI6MDA6MDAgQU0gKzA0OjAwIiwiQVJDSElWRSI6InRydWUiLCJpYXQiOjE3MzE0MDAxMTMsIm5iZiI6MTczMTQwMDExM30.a1shRx8Z1AaOS6owhs3TgCr4jlBTir7qgV52QcoHmUhO9QZzPsEocDASPPrIiefitpVq-A7tpjiXR214xaSwqAtZjVCv0xWtsHxQ_ciO5Whyuw4TjyTlvuIziolfwhopmX0bOX0GJOkpKOW_HRU1nHhwiAyqtbV5o7vecG8btEZlWaAm4CvWEQZEAPW-1bj65lIU0dzcZJGBGuckeIeuZXZynFHsO_5KJNhmO7ZQKm-RkvRG-8aVQb8L9jkR3FM6COxcNJl6JWEdt3q_jUJ45T57PZ-sJ3bG64EkhbJtTrCD-3msgCWF0-QcV7TqOakA5ClyedsgtoB6Qonb-m5iAg" },
		{ "NONE", "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOiJlc2RiIiwiaXNzIjoiZXNkYiIsImV4cCI6MTgyNDQ1NjgyOSwianRpIjoiOTVmZTY2YzAtMDRkMi00MjExLWI1ZGQtNTAyM2MyYTAxMGFiIiwic3ViIjoiRVNEQiBUZXN0cyIsIklzVHJpYWwiOiJUcnVlIiwiSXNFeHBpcmVkIjoiRmFsc2UiLCJJc1ZhbGlkIjoiVHJ1ZSIsIklzRmxvYXRpbmciOiJUcnVlIiwiRGF5c1JlbWFpbmluZyI6IjEiLCJTdGFydERhdGUiOiIyNi8wNC8yMDI0IDAwOjAwOjAwICswMTowMCIsIk5PTkUiOiJ0cnVlIiwiaWF0IjoxNzI5ODQ4ODI5LCJuYmYiOjE3Mjk4NDg4Mjl9.R24i-ZAow3BhRaST3n25Uc_nQ184k83YRZZ0oRcWbU9B9XNLRH0Iegj0HmkyzkT50I4gcIJOIfcO6mIPp4Y959CP7aTAlt7XEnXoGF0GwsfXatAxy4iXG8Gpya7INgMoWEeN0v8eDH8_OVmnieOxeba9ex5j1oAW_FtQDMzcFjAeErpW__8zmkCsn6GzvlhdLE4e3r2wjshvrTTcS_1fvSVjQZov5ce2sVBJPegjCLO_QGiIBK9QTnpHrhe6KCYje6fSTjgty0V1Qj22bftvrXreYzQijPrnC_ek1BwV-A1JvacZugMCPIy8WvE5jE3hVYRWGGUzQZ-CibPGsjudYA" },
	};

	public FakeLicenseService(bool createLicense = true, string? entitlement = null) {
		var license = new License(new JsonWebToken(_hardCodedTokens[entitlement ?? "NONE"]));
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
