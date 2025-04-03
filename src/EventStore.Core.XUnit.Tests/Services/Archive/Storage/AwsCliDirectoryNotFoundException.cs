// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;

namespace EventStore.Core.XUnit.Tests.Services.Archive.Storage;

public sealed class AwsCliDirectoryNotFoundException(string path)
	: DirectoryNotFoundException($"Directory '{path}' with config files for AWS CLI doesn't exist");
