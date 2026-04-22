// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography.X509Certificates;
using KurrentDB.Common.Utils;

namespace KurrentDB.Core.Certificates;

public static class PubliclyTrustedCertificateSelector {
	public static bool ShouldServe(X509Certificate2 publiclyTrustedCertificate, string sniHostname) =>
		publiclyTrustedCertificate != null
		&& !string.IsNullOrEmpty(sniHostname)
		&& publiclyTrustedCertificate.MatchesName(sniHostname);
}
