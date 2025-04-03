// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Grpc;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Grpc;

public class PositionTests {
	[Test]
	public void Equality() {
		var sut = new Position(6, 5);

		Assert.AreEqual(new Position(6, 5), sut);
	}

	[Test]
	public void Inequality() {
		var sut = new Position(6, 5);

		Assert.AreNotEqual(new Position(7, 6), sut);
	}

	[Test]
	public void EqualityOperator() {
		var sut = new Position(6, 5);

		Assert.True(new Position(6, 5) == sut);
	}

	[Test]
	public void InequalityOperator() {
		var sut = new Position(6, 5);

		Assert.True(new Position(7, 6) != sut);
	}

	public static IEnumerable<object[]> ArgumentOutOfRangeTestCases() {
		const string commitPosition = nameof(commitPosition);
		const string preparePosition = nameof(preparePosition);

		yield return new object[] {5UL, 6UL, commitPosition};
		yield return new object[] {ulong.MaxValue - 1, 6UL, commitPosition};
		yield return new object[] {ulong.MaxValue, ulong.MaxValue - 1, preparePosition};
		yield return new object[] {(ulong)long.MaxValue + 1, (ulong)long.MaxValue, commitPosition};
	}

	[TestCaseSource(nameof(ArgumentOutOfRangeTestCases))]
	public void ArgumentOutOfRange(ulong commitPosition, ulong preparePosition, string name) {
		var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new Position(commitPosition, preparePosition));
		Assert.AreEqual(name, ex.ParamName);
	}

	public static IEnumerable<object[]> GreaterThanTestCases() {
		yield return new object[] {new Position(6, 6), new Position(6, 5)};
		yield return new object[] {Position.End, new Position(ulong.MaxValue, 0),};
	}

	[TestCaseSource(nameof(GreaterThanTestCases))]
	public void GreaterThan(Position left, Position right) => Assert.True(left > right);

	public static IEnumerable<object[]> GreaterThanOrEqualToTestCases()
		=> GreaterThanTestCases().Concat(new[] {new object[] {Position.Start, Position.Start}});

	[TestCaseSource(nameof(GreaterThanOrEqualToTestCases))]
	public void GreaterThanOrEqualTo(Position left, Position right) => Assert.True(left >= right);

	public static IEnumerable<object[]> LessThanTestCases() {
		yield return new object[] {new Position(6, 5), new Position(6, 6)};
		yield return new object[] {new Position(ulong.MaxValue, 0), Position.End,};
	}

	[TestCaseSource(nameof(LessThanTestCases))]
	public void LessThan(Position left, Position right) => Assert.True(left < right);

	public static IEnumerable<object[]> LessThanOrEqualToTestCases()
		=> LessThanTestCases().Concat(new[] {new object[] {Position.End, Position.End}});

	[TestCaseSource(nameof(LessThanOrEqualToTestCases))]
	public void LessThanOrEqualTo(Position left, Position right) => Assert.True(left <= right);

	public static IEnumerable<object[]> CompareToTestCases() {
		yield return new object[] {new Position(6, 5), new Position(6, 6), -1};
		yield return new object[] {new Position(6, 6), new Position(6, 5), 1};
		yield return new object[] {new Position(6, 6), new Position(6, 6), 0};
	}

	[TestCaseSource(nameof(CompareToTestCases))]
	public void CompareTo(Position left, Position right, int expected) =>
		Assert.AreEqual(expected, left.CompareTo(right));
}
