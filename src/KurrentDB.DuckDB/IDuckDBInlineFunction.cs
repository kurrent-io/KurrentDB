// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;

namespace KurrentDB.DuckDB;

public interface IDuckDBInlineFunction {
	void Register(DuckDBConnection connection);
}
