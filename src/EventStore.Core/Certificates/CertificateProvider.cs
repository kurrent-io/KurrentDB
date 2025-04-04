// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography.X509Certificates;

namespace EventStore.Core.Certificates;

public abstract class CertificateProvider {
	public X509Certificate2 Certificate;
	public X509Certificate2Collection IntermediateCerts;
	public X509Certificate2Collection TrustedRootCerts;
	public abstract LoadCertificateResult LoadCertificates(ClusterVNodeOptions options);
	public abstract string GetReservedNodeCommonName();
}

public enum LoadCertificateResult {
	Success = 1,
	VerificationFailed,
	Skipped
}
