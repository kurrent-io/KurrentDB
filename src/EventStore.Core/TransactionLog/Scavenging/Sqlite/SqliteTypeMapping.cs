// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite;

public static class SqliteTypeMapping {
	private static readonly Dictionary<Type, SqliteType> _sqliteTypeMap = new Dictionary<Type, SqliteType>() {
		{typeof(int), SqliteType.Integer},
		{typeof(float), SqliteType.Real},
		{typeof(long), SqliteType.Integer},
		{typeof(ulong), SqliteType.Integer},
		{typeof(string), SqliteType.Text},
	};
	
	/// <summary>
	/// Returns the mapped SqliteType. 
	/// </summary>
	public static SqliteType Map<T>() {
		return _sqliteTypeMap[typeof(T)];
	}
	
	/// <summary>
	/// Returns the name of the mapped type.
	/// </summary>
	public static string GetTypeName<T>() {
		return Map<T>().ToString().ToUpper();
	}
}
