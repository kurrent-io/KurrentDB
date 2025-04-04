// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable 1591
namespace KurrentDB.TestClient;

/// <summary>
/// Standalone command executable within the test client
/// </summary>
public interface ICmdProcessor {
	/// <summary>
	/// Keyword associated with this command (to start it)
	/// </summary>
	string Keyword { get; }

	/// <summary>
	/// Short usage string with optional and required parameters
	/// </summary>
	string Usage { get; }

	bool Execute(CommandProcessorContext context, string[] args);
}
