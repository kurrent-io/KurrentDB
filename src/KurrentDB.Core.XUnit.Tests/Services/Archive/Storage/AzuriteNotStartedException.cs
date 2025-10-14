// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Core.XUnit.Tests.Services.Archive.Storage;

public sealed class AzuriteNotStartedException : Exception {
	public AzuriteNotStartedException()
		: base("Azurite Emulator is not started locally") {
		HelpLink = "https://learn.microsoft.com/en-us/azure/storage/common/storage-install-azurite";
	}
}
