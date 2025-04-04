// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;

namespace KurrentDB.TestClient.Commands;

internal class ExitProcessor : ICmdProcessor {
	private readonly CancellationTokenSource _cancellationTokenSource;

	public ExitProcessor(CancellationTokenSource cancellationTokenSource) {
		_cancellationTokenSource = cancellationTokenSource;
	}

	public string Usage {
		get { return Keyword; }
	}

	public string Keyword {
		get { return "EXIT"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		context.Log.Information("Exiting...");
		_cancellationTokenSource.Cancel();
		return true;
	}
}
