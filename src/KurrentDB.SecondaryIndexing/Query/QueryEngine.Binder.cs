// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kurrent.Quack;
using static System.Buffers.Binary.BinaryPrimitives;

namespace KurrentDB.SecondaryIndexing.Query;

partial class QueryEngine {
	[StructLayout(LayoutKind.Auto)]
	private readonly ref struct QueryBinder(ref readonly PreparedStatement statement) : IPreparedQueryBinder {
		private readonly ref readonly PreparedStatement _statement = ref statement;

		public void Bind(int index, scoped ReadOnlySpan<byte> value, ParameterType type) {
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
				case ParameterType.Int8:
					_statement.Bind(index, (sbyte)value[0]);
					break;
				case ParameterType.UInt8:
					_statement.Bind(index, value[0]);
					break;
				case ParameterType.Int32:
					_statement.Bind(index, ReadInt32LittleEndian(value));
					break;
				case ParameterType.UInt32:
					_statement.Bind(index, ReadUInt32LittleEndian(value));
					break;
				case ParameterType.Int64:
					_statement.Bind(index, ReadInt64LittleEndian(value));
					break;
				case ParameterType.UInt64:
					_statement.Bind(index, ReadUInt64LittleEndian(value));
					break;
				case ParameterType.Int128:
					_statement.Bind(index, ReadInt128LittleEndian(value));
					break;
				case ParameterType.UInt128:
					_statement.Bind(index, ReadUInt128LittleEndian(value));
					break;
				case ParameterType.Single:
					_statement.Bind(index, ReadSingleBigEndian(value));
					break;
				case ParameterType.Double:
					_statement.Bind(index, ReadDoubleBigEndian(value));
					break;
				default:
					_statement.Bind(index, DBNull.Value);
					break;
			}
		}
	}
}
