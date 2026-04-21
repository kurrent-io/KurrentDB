// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Query;

public enum ParameterType {
	Null = 0,
	Utf8String,
	Blob,
	Boolean,
	Int8,
	UInt8,
	Int32,
	UInt32,
	Int64,
	UInt64,
	Int128,
	UInt128,
	Single,
	Double,
}
