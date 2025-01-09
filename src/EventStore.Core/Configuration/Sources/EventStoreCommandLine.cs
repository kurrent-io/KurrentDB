// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

#nullable enable

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using static System.StringComparer;

namespace EventStore.Core.Configuration.Sources;

public class EventStoreCommandLineConfigurationProvider(IEnumerable<string> args)
	: CommandLineConfigurationProvider(args) {

	public override void Load() {
		base.Load();

		Data = Data.Keys
			.Where(KurrentConfigurationKeys.IsEventStoreKey)
			.ToDictionary(
				KurrentConfigurationKeys.NormalizeEventStorePrefix,
				x => Data[x], OrdinalIgnoreCase
			);
	}
}

public class CommandLineConfigurationSource : IConfigurationSource {
	public CommandLineConfigurationSource(string[] args) {
		Args = args
			.Select(KurrentDBCommandLineConfigurationSource.NormalizeKeys)
			.Select((x, i) => KurrentDBCommandLineConfigurationSource.NormalizeBooleans(args, x, i));
	}

	private IEnumerable<string> Args { get; set; }

	public IConfigurationProvider Build(IConfigurationBuilder builder) =>
		new EventStoreCommandLineConfigurationProvider(Args);
}

public static class EventStoreCommandLineConfigurationExtensions {
	public static IConfigurationBuilder AddLegacyEventStoreCommandLine(this IConfigurationBuilder builder, params string[] args) =>
		builder.Add(new CommandLineConfigurationSource(args));
}
