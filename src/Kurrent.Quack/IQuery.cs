// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;

namespace Kurrent.Quack;

/// <summary>
/// A marker interface for the query.
/// </summary>
/// <typeparam name="TArgs"></typeparam>
/// <typeparam name="TRow"></typeparam>
public interface IQuery<TArgs, out TRow> : IPreparedStatement<TArgs>, IDataRowParser<TRow>
	where TArgs : struct
	where TRow : struct;

/// <summary>
/// A marker interface for the query without parameter.
/// </summary>
/// <typeparam name="TRow">The type of the row.</typeparam>
public interface IQuery<out TRow> : IParameterlessStatement, IQuery<ValueTuple, TRow> where TRow: struct;
