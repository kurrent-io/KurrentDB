// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.XUnit.Tests;

namespace KurrentDB.Duck;

public abstract class DuckDbTests<T> : DirectoryPerTest<T>
	where T : DuckDbTests<T>
{
	protected string ConnectionString => $"Data Source={Fixture.GetFilePathFor("test.db")};";
}
