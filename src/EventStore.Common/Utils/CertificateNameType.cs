// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Common.Utils;

public static class CertificateNameType {
	// Based on RFC 5280 (https://datatracker.ietf.org/doc/html/rfc5280)
	public const string IpAddress = "iPAddress";
	public const string DnsName = "dNSName";
}
