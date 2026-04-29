// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Common.Utils;

// The certificate profile distinguishes whether a certificate is configured to authenticate a node or a user.
// It does not imply that the certificate is valid (trusted, non expired etc).
// It does not imply that the user/node is authenticated.
public enum CertificateClassification {
	Unclassified,
	Node,
	User,
}
