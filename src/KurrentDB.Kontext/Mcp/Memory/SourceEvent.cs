// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;

namespace KurrentDB.Kontext.Mcp.Memory;

public sealed record SourceEvent(
	[property: Description("Stream the source event lives on.")]
	string Stream,
	[property: Description("Event number within the stream.")]
	long EventNumber);