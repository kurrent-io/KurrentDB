// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;

namespace KurrentDB.TestClient.Commands;

internal class UsageProcessor : ICmdProcessor {
	public string Keyword {
		get { return "USAGE"; }
	}

	public string Usage {
		get { return "USAGE"; }
	}

	private readonly CommandsProcessor _commands;

	public UsageProcessor(CommandsProcessor commands) {
		_commands = commands;
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		var allCommands = _commands.RegisteredProcessors.Select(x => x.Usage.ToUpper());
		context.Log.Information("Available commands:");

		foreach (var command in allCommands) {
			context.Log.Information("    {command}", command);
		}

		return true;
	}
}
