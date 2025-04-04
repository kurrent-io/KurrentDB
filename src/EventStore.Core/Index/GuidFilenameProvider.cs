// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;

namespace EventStore.Core.Index;

public class GuidFilenameProvider : IIndexFilenameProvider {
	private readonly string _directory;

	public GuidFilenameProvider(string directory) {
		_directory = directory;
	}

	public string GetFilenameNewTable() {
		return Path.Combine(_directory, Guid.NewGuid().ToString());
	}
}
