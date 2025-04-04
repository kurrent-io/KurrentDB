// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Common.DevCertificates;

public enum EnsureCertificateResult {
	Succeeded = 1,
	ValidCertificatePresent,
	ErrorCreatingTheCertificate,
	ErrorSavingTheCertificateIntoTheCurrentUserPersonalStore,
	ErrorExportingTheCertificate,
	FailedToTrustTheCertificate,
	UserCancelledTrustStep,
	FailedToMakeKeyAccessible,
}
