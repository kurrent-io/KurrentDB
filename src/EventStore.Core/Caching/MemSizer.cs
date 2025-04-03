// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using EventStore.Common.Utils;

namespace EventStore.Core.Caching;

public class MemSizer {
	public const int ObjectHeaderSize = 16;
	public const int ArraySize = 24;
	public const int StringSize = 20;
	private const int DictionaryEntryOverhead = 12;
	private const int LinkedListNodeOverhead = 40;
	public const int LinkedListEntrySize = 0;

	public static int SizeOf(string s) {
		if (string.IsNullOrEmpty(s))
			return 0;
		return (StringSize + s.Length * 2 + 1).RoundUpToMultipleOf(IntPtr.Size);
	}

	public static int SizeOf(string[] arr) {
		if (arr == null)
			return 0;

		var size = 0;
		size += ArraySize; // string array
		size += arr.Length * Unsafe.SizeOf<string>(); // string refs
		size += arr.Sum(static x => SizeOf(x)); // strings
		return size;
	}

	public static int SizeOfDictionaryEntry<TKey, TValue>() =>
		DictionaryEntryOverhead + (Unsafe.SizeOf<TKey>() + Unsafe.SizeOf<TValue>()).RoundUpToMultipleOf(IntPtr.Size);

	public static int SizeOfLinkedListNode<T>() =>
		LinkedListNodeOverhead + Unsafe.SizeOf<T>().RoundUpToMultipleOf(IntPtr.Size);
}
