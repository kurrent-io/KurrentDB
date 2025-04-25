// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KurrentDB.Duck;

/// <summary>
/// Represents the typed result of the query.
/// </summary>
/// <remarks>
/// The caller must call <see cref="GetEnumerator()"/> only once to release the unmanaged resources.
/// </remarks>
/// <param name="result"></param>
/// <typeparam name="TRow"></typeparam>
/// <typeparam name="TParser"></typeparam>
[StructLayout(LayoutKind.Auto)]
public readonly struct StreamQueryResult<TRow, TParser>(StreamQueryResult result)
	where TRow : struct, ITuple
	where TParser : IRowParser<TRow> {

	public Enumerator GetEnumerator() => new(result);

	[StructLayout(LayoutKind.Auto)]
	public struct Enumerator(StreamQueryResult result) : IDisposable {
		private DataChunk _chunk;
		private TRow _current;
		private bool _initialized;

		public bool MoveNext() {
			if (!_initialized) {
				if (!result.TryFetch(out _chunk))
					return false;

				_initialized = true;
			}

			return _chunk.TryRead<TRow, TParser>(out _current) || result.TryFetch(out _chunk);
		}

		public readonly TRow Current => _current;

		public void Dispose() => result.Dispose();
	}
}
