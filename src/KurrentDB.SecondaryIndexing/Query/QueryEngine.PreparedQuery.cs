// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DotNext.Buffers;
using DotNext.IO;
using DotNext.Text;

namespace KurrentDB.SecondaryIndexing.Query;

partial class QueryEngine {
	private static readonly MemoryAllocator<byte> ArrayPoolAllocator = ArrayPool<byte>.Shared.ToAllocator();

	// Binary format:
	// 1 byte - flags
	// 4 bytes (Q) - rewritten query length
	// Q bytes - rewritten query UTF-8 encoded
	// 4 bytes (U) - the number of user-defined views:
	//		4 bytes (L) - view name length
	//		L bytes - view name UTF-8 encoded
	// N bytes (optional) - digital signature
	[StructLayout(LayoutKind.Auto)]
	private ref struct PreparedQueryBuilder() {
		private readonly List<string> _userIndices = new();

		public bool HasDefaultIndex;

		public readonly void AddUserIndexViewName(string viewName) => _userIndices.Add(viewName);

		public MemoryOwner<byte> Build(ReadOnlySpan<byte> queryUtf8, ReadOnlySpan<byte> signatureKey) {
			var writer = new BufferWriterSlim<byte>(1024, ArrayPoolAllocator);
			try {
				// Header
				writer.Add(GetFlags(HasDefaultIndex));

				// rewritten query
				writer.WriteLittleEndian(queryUtf8.Length);
				writer.Write(queryUtf8);

				// Views
				writer.WriteLittleEndian(_userIndices.Count);

				var encodingContext = new EncodingContext(Encoding.UTF8, reuseEncoder: true);
				foreach (var viewName in _userIndices) {
					writer.Encode(viewName, in encodingContext, LengthFormat.LittleEndian);
				}

				// Digital Signature
				if (!signatureKey.IsEmpty) {
					Sign(ref writer, signatureKey);
				}

				return writer.DetachOrCopyBuffer();
			} finally {
				writer.Dispose();
			}

			static byte GetFlags(bool hasDefaultIndex) {
				var result = PreparedQuery.EmptyFlags;

				if (hasDefaultIndex) {
					result |= PreparedQuery.HasDefaultIndexFlag;
				}

				return result;
			}

			static void Sign(ref BufferWriterSlim<byte> writer, ReadOnlySpan<byte> signatureKey) {
				var payload = writer.WrittenSpan;
				var hash = writer.GetSpan(HMACSHA256.HashSizeInBytes);
				writer.Advance(HMACSHA256.HashData(signatureKey, payload, hash));
			}
		}
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly ref struct PreparedQuery {
		public const byte EmptyFlags = 0;
		public const byte HasDefaultIndexFlag = 1;

		public readonly bool HasDefaultIndex;
		public readonly ReadOnlySpan<byte> Query;
		private readonly int _viewNameCount;
		private readonly ReadOnlySpan<byte> _viewNames;
		private readonly ReadOnlySpan<byte> _signature;
		private readonly ReadOnlySpan<byte> _payload;

		public PreparedQuery(ReadOnlySpan<byte> preparedQuery) {
			var reader = new SpanReader<byte>(preparedQuery);
			var flags = reader.Read();
			HasDefaultIndex = (flags & HasDefaultIndexFlag) is not 0;

			// Query
			var length = reader.ReadLittleEndian<int>();
			Query = reader.Read(length);

			// View names
			_viewNameCount = reader.ReadLittleEndian<int>();
			_viewNames = reader.RemainingSpan;

			// Signature (last 32 bytes). If prepared query is not signed, it could be just last 32 bytes, which is fine
			// because the caller is knows for sure is the query should be signed or not
			var offset = preparedQuery.Length - HMACSHA256.HashSizeInBytes;
			if (offset >= 0) {
				_payload = preparedQuery.Slice(0, offset);
				_signature = preparedQuery.Slice(offset);
			} else {
				_payload = preparedQuery;
				_signature = ReadOnlySpan<byte>.Empty;
			}
		}

		public int ViewCount => _viewNameCount;

		public ViewNameEnumerator ViewNames => new(_viewNameCount, _viewNames);

		public bool CheckIntegrity(ReadOnlySpan<byte> signatureKey) {

			Span<byte> actual = stackalloc byte[HMACSHA256.HashSizeInBytes];
			HMACSHA256.HashData(signatureKey, _payload, actual);

			// compare hashes
			return actual.SequenceEqual(_signature);
		}
	}

	[StructLayout(LayoutKind.Auto)]
	private ref struct ViewNameEnumerator {
		private SpanReader<byte> _reader;
		private ReadOnlySpan<byte> _current;
		private int _entryCount;

		public ViewNameEnumerator(int entryCount, ReadOnlySpan<byte> reader) {
			_reader = new(reader);
			_entryCount = entryCount;
		}

		public readonly ReadOnlySpan<byte> Current => _current;

		public static bool MoveNext(scoped ref SpanReader<byte> reader, scoped ref int remainingCount, out ReadOnlySpan<byte> current) {
			if (remainingCount <= 0) {
				current = default;
				return false;
			}

			remainingCount--;
			var length = reader.ReadLittleEndian<int>();
			current = reader.Read(length);
			return true;
		}

		public bool MoveNext() => MoveNext(ref _reader, ref _entryCount, out _current);
	}
}
