// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.CompilerServices;

namespace KurrentDB.Common.Utils;

public interface IValidator<T> {
	void Validate(T t);
}

public static class Ensure {
	public static T NotNull<T>(T argument, [CallerArgumentExpression("argument")] string argumentName = null) where T : class {
		ArgumentNullException.ThrowIfNull(argument, argumentName);
		return argument;
	}

	public static string NotNullOrEmpty(string argument, [CallerArgumentExpression("argument")] string argumentName = null) {
		return string.IsNullOrEmpty(argument) ? throw new ArgumentNullException(argument, argumentName) : argument;
	}

	public static int Positive(int number, [CallerArgumentExpression("number")] string argumentName = null) {
		if (number <= 0)
			throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} should be positive.");
		return number;
	}

	public static long Positive(long number, [CallerArgumentExpression("number")] string argumentName = null) {
		if (number <= 0)
			throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} should be positive.");
		return number;
	}

	public static long Nonnegative(long number, [CallerArgumentExpression("number")] string argumentName = null) {
		if (number < 0)
			throw new ArgumentOutOfRangeException(argumentName, argumentName + " should be non negative.");
		return number;
	}

	public static int Nonnegative(int number, [CallerArgumentExpression("number")] string argumentName = null) {
		return number < 0 ? throw new ArgumentOutOfRangeException(argumentName, argumentName + " should be non negative.") : number;
	}

	public static double Nonnegative(double number, [CallerArgumentExpression("number")] string argumentName = null) {
		return number < 0 ? throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} should be non negative.") : number;
	}

	public static Guid NotEmptyGuid(Guid guid, [CallerArgumentExpression("guid")] string argumentName = null) {
		return Guid.Empty == guid ? throw new ArgumentException(argumentName, $"{argumentName} should be non-empty GUID.") : guid;
	}

	public static void Equal(int expected, int actual, [CallerArgumentExpression("actual")] string argumentName = null) {
		if (expected != actual)
			throw new ArgumentException($"{argumentName} expected value: {expected}, actual value: {actual}");
	}

	public static void Equal(long expected, long actual, [CallerArgumentExpression("actual")] string argumentName = null) {
		if (expected != actual)
			throw new ArgumentException($"{argumentName} expected value: {expected}, actual value: {actual}");
	}

	public static void Equal(bool expected, bool actual, [CallerArgumentExpression("actual")] string argumentName = null) {
		if (expected != actual)
			throw new ArgumentException($"{argumentName} expected value: {expected}, actual value: {actual}");
	}

	public static void Valid<T>(T t, IValidator<T> validator) {
		validator?.Validate(t);
	}
}
