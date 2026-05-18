// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Query;

public interface IPreparedQueryBinder {
	void Bind(int index, scoped ReadOnlySpan<byte> value, ParameterType type);
}
