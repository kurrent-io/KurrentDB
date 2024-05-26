// ReSharper disable PossibleMultipleEnumeration
// ReSharper disable CheckNamespace

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace EventStore.Streaming;

[PublicAPI]
static class Ensure {
	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T NotNull<T>(T? argument, [CallerArgumentExpression("argument")] string? argumentName = default) where T : class {
		ArgumentNullException.ThrowIfNull(argument, argumentName);
		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T NotNull<T>(T? argument, [CallerArgumentExpression("argument")] string? argumentName = default) where T : struct =>
		argument ?? throw new ArgumentNullException(argumentName);

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ReadOnlySpan<T> NotNullOrEmpty<T>(ReadOnlySpan<T> argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument == null)
			throw new ArgumentNullException(argumentName);

		if (argument.IsEmpty)
			throw new ArgumentException($"{argumentName} must not be empty", argumentName);

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ReadOnlyMemory<T> NotEmpty<T>(ReadOnlyMemory<T> argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument.IsEmpty)
			throw new ArgumentException($"{argumentName} must not be empty", argumentName);

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Span<T> NotNullOrEmpty<T>(Span<T> argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument == null)
			throw new ArgumentNullException(argumentName);

		if (argument.IsEmpty)
			throw new ArgumentException($"{argumentName} must not be empty", argumentName);

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Memory<T> NotEmpty<T>(Memory<T> argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument.IsEmpty)
			throw new ArgumentException($"{argumentName} must not be empty", argumentName);

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string NotNullOrEmpty(string? argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (string.IsNullOrEmpty(argument))
			throw new ArgumentNullException(argumentName);

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static IEnumerable<T> NotNullOrEmpty<T>(IEnumerable<T> argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		ArgumentNullException.ThrowIfNull(argument, argumentName);

		if (!argument.Any())
			throw new ArgumentException($"{argumentName} must not be empty", argumentName);

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T[] NotNullOrEmpty<T>(T[] argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		ArgumentNullException.ThrowIfNull(argument, argumentName);

		if (argument.Length == 0)
			throw new ArgumentException($"{argumentName} must not be empty", argumentName);

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string NotNullOrWhiteSpace(string? argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (string.IsNullOrWhiteSpace(argument))
			throw new ArgumentNullException(argumentName);

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static char NotNull(char argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument <= 0)
			throw new ArgumentOutOfRangeException(argumentName);

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Positive(int argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument <= 0)
			throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} must be positive.");

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static long Positive(long argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument <= 0)
			throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} must be positive.");

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double Positive(double argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument <= 0)
			throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} must be positive.");

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TimeSpan Positive(TimeSpan argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument.TotalMilliseconds <= 0)
			throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} must be positive.");

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Negative(int argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument > 0)
			throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} must be negative.");

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static long Negative(long argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument > 0)
			throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} must be negative.");

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static long NonNegative(long argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument < 0)
			throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} must be non-negative.");

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int NonNegative(int argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument < 0)
			throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} must be non-negative.");

		return argument;
	}

	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Guid NotEmpty(Guid argument, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (Guid.Empty == argument)
			throw new ArgumentException($"{argumentName} must not be empty.", argumentName);

		return argument;
	}
	
	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T NotDefault<T>(T argument, T defaultValue, [CallerArgumentExpression("argument")] string? argumentName = default) {
		if (argument?.Equals(defaultValue) ?? true)
			throw new ArgumentException($"{argumentName} must not be {defaultValue}.", argumentName);

		return argument;
	}
	
	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T NotOneOf<T>(T argument, T[] values, [CallerArgumentExpression("argument")] string? argumentName = default) {
		ArgumentNullException.ThrowIfNull(argument, argumentName);
		
		if (values.Any(v => v?.Equals(argument) ?? true))
			throw new ArgumentException($"{argumentName} must not be one of {string.Join(", ", values)}.", argumentName);
		
		return argument;
	}
	
	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T NotOneOf<T>(T argument, T value, [CallerArgumentExpression("argument")] string? argumentName = default) =>
		NotOneOf(argument, [value], argumentName);
	
	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static IEnumerable<T> ItemsAreValid<T>(IEnumerable<T> argument, Predicate<T> predicate, [CallerArgumentExpression("argument")] string? argumentName = default) {
		ArgumentNullException.ThrowIfNull(argument, argumentName);
		ArgumentNullException.ThrowIfNull(predicate);
		
		var invalidItemsCount = argument.Count(item => !predicate(item));

		if (invalidItemsCount > 0)
			throw new ArgumentException($"{argumentName} contains {invalidItemsCount} invalid items.", argumentName);
		
		return argument;
	}
	
	[DebuggerHidden]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static IEnumerable<T> ItemsAreValid<T>(IEnumerable<T> argument, Predicate<T> predicate, Func<T, object> getItemId, [CallerArgumentExpression("argument")] string? argumentName = default) {
		ArgumentNullException.ThrowIfNull(argument, argumentName);
		ArgumentNullException.ThrowIfNull(predicate);
		ArgumentNullException.ThrowIfNull(getItemId);

		var invalidItems = argument.Where(item => !predicate(item)).Select(getItemId).ToArray();
		
		if (invalidItems.Length > 0)
			throw new ArgumentException($"{argumentName} contains {invalidItems.Length} invalid items: {string.Join(", ", invalidItems)}", argumentName);
		
		return argument;
	}
    
    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Valid<T>(T argument, Predicate<T> predicate, [CallerArgumentExpression("argument")] string? argumentName = default) {
        ArgumentNullException.ThrowIfNull(argument, argumentName);
        ArgumentNullException.ThrowIfNull(predicate);

        if (!predicate(argument))
            throw new ArgumentException($"{argumentName} is invalid.", argumentName);
		
        return argument;
    }
}