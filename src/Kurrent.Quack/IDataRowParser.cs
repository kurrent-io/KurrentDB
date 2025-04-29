// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;

namespace Kurrent.Quack;

/// <summary>
/// Represents structured row parser.
/// </summary>
/// <typeparam name="T">The type of the row.</typeparam>
public interface IDataRowParser<out T> where T : struct, ITuple {
	static abstract T Parse(ref DataChunk.Row row);
}
