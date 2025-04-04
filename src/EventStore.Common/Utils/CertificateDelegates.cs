// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace EventStore.Common.Utils;

public static class CertificateDelegates {
	public delegate (bool, string) ServerCertificateValidator(X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors, string[] otherNames);
	public delegate (bool, string) ClientCertificateValidator(X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors);
}
