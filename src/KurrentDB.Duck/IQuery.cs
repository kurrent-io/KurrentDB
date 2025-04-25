// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;

namespace KurrentDB.Duck;

/// <summary>
/// Represents the prepared query with formal parameters and the row schema.
/// </summary>
/// <typeparam name="TInput"></typeparam>
/// <typeparam name="TOutput"></typeparam>
public interface IQuery<TInput, out TOutput> : IPreparedStatement<TInput>, IRowParser<TOutput>
	where TInput : struct, ITuple
	where TOutput : struct, ITuple;

/// <summary>
/// Represents the prepared query without formal parameters and the row schema.
/// </summary>
/// <typeparam name="TOutput"></typeparam>
public interface IQuery<out TOutput> : IParameterlessStatement, IQuery<ValueTuple, TOutput>
	where TOutput : struct, ITuple;
