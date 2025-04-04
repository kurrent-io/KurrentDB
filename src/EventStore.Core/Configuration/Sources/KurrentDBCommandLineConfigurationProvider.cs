// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration.CommandLine;
using static System.StringComparer;

namespace EventStore.Core.Configuration.Sources;

public class KurrentDBCommandLineConfigurationProvider(IEnumerable<string> args)
	: CommandLineConfigurationProvider(args) {

	public override void Load() {
		base.Load();

		Data = Data.Keys
			.ToDictionary(
				KurrentConfigurationKeys.Normalize,
				x => Data[x], OrdinalIgnoreCase
			);
	}
}
