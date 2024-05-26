using System.Diagnostics.CodeAnalysis;

namespace EventStore.Streaming;

public static class LinkedListExtensions {
	public static LinkedListNode<T> AddUniqueFirst<T>(this LinkedList<T> list, [DisallowNull] T value) {
		if (list.Contains(value))
			throw new ArgumentException($"{list.GetType().Name} already added");

		return list.AddFirst(value);
	}

	public static LinkedListNode<T> AddUniqueLast<T>(this LinkedList<T> list, [DisallowNull] T value) {
		if (list.Contains(value))
			throw new ArgumentException($"{value.GetType().Name} already added");

		return list.AddLast(value);
	}
	
	public static bool TryAddUniqueFirst<T>(this LinkedList<T> list, [DisallowNull] T value) {
		var exists = list.Any(x => x!.GetType() == typeof(T));
		
		if (!exists)
			list.AddUniqueFirst(value);

		return exists;
	}
}