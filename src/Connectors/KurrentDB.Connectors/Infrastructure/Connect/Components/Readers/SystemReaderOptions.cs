// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using KurrentDB.Core.Bus;
using Kurrent.Surge.Readers.Configuration;

namespace KurrentDB.Connect.Readers.Configuration;

[PublicAPI]
public record SystemReaderOptions : ReaderOptions {
	public IPublisher Publisher { get; init; }
}
