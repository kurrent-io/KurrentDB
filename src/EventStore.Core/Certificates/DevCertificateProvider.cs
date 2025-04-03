// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;

namespace EventStore.Core.Certificates;

public class DevCertificateProvider : CertificateProvider {
	public DevCertificateProvider(X509Certificate2 certificate) {
		Certificate = certificate;
		TrustedRootCerts = new X509Certificate2Collection(certificate);
	}
	public override LoadCertificateResult LoadCertificates(ClusterVNodeOptions options) {
		return LoadCertificateResult.Skipped;
	}

	public override string GetReservedNodeCommonName() {
		return Certificate.GetCommonName();
	}
}
