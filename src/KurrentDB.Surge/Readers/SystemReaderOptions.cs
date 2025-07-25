// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Readers.Configuration;
using KurrentDB.Core.Bus;

namespace KurrentDB.Surge.Readers;

[PublicAPI]
public record SystemReaderOptions : ReaderOptions {
	public IPublisher Publisher { get; init; } = null!;
}
