// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Licensing;

// Signals that no license key was configured. Distinct from a validation failure: it is a
// legitimate configuration, not an error to warn about.
public class NoLicenseKeyException : Exception;
