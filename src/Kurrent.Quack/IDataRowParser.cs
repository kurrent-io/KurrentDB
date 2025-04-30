// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Quack;

/// <summary>
/// Represents structured row parser.
/// </summary>
/// <typeparam name="T">The type of the row.</typeparam>
public interface IDataRowParser<out T> where T : struct {
	static abstract T Parse(ref DataChunk.Row row);
}
