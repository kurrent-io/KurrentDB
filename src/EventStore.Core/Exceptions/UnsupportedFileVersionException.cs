// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Exceptions;

public class UnsupportedFileVersionException : Exception {
	public UnsupportedFileVersionException(string filename, byte fileVersion, byte lastSupportedVersion,
		string message = "")
		: base(string.Format("File {0} has unsupported version: {1}. The latest supported version is: {2}. {3}",
			filename,
			fileVersion,
			lastSupportedVersion,
			message)) {
	}
}
