// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;

namespace KurrentDB.Core.XUnit.Tests.Services.Archive.Storage;

public sealed class GcpCliDirectoryNotFoundException(string path)
	: DirectoryNotFoundException($"Directory '{path}' with config files for Google Cloud CLI doesn't exist");
