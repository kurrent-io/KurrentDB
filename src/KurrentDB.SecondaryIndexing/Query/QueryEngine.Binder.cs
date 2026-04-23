// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Query;

partial class QueryEngine {
	[StructLayout(LayoutKind.Auto)]
	private ref struct QueryBinder(ref readonly PreparedStatement statement) : IPreparedQueryBinder {
		private readonly ref readonly PreparedStatement _statement = ref statement;

		public void Bind(int index, ReadOnlySpan<byte> value, ParameterType type) {
			switch (type) {
				case ParameterType.Blob:
					_statement.Bind(index, value, BlobType.Raw);
					break;
				case ParameterType.Boolean:
					_statement.Bind(index, Unsafe.BitCast<byte, bool>(value[0]));
					break;
				case ParameterType.Utf8String:
					_statement.Bind(index, value, BlobType.Utf8);
					break;
				case ParameterType.Int32:
					_statement.Bind(index, BinaryPrimitives.ReadInt32LittleEndian(value));
					break;
				case ParameterType.UInt32:
					_statement.Bind(index, BinaryPrimitives.ReadUInt32LittleEndian(value));
					break;
				case ParameterType.Int64:
					_statement.Bind(index, BinaryPrimitives.ReadInt64LittleEndian(value));
					break;
				case ParameterType.UInt64:
					_statement.Bind(index, BinaryPrimitives.ReadUInt64LittleEndian(value));
					break;
				case ParameterType.Int128:
					_statement.Bind(index, BinaryPrimitives.ReadInt128LittleEndian(value));
					break;
				case ParameterType.UInt128:
					_statement.Bind(index, BinaryPrimitives.ReadUInt128LittleEndian(value));
					break;
				default:
					_statement.Bind(index, DBNull.Value);
					break;
			}
		}
	}
}
