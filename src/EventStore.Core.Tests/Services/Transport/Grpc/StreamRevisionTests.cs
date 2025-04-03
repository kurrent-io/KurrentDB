// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Services.Transport.Common;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Grpc;

public class StreamRevisionTests {
	[Test]
	public void Equality() {
		var sut = new StreamRevision(1);
		Assert.AreEqual(new StreamRevision(1), sut);
	}

	[Test]
	public void Inequality() {
		var sut = new StreamRevision(1);
		Assert.AreNotEqual(new StreamRevision(2), sut);
	}

	[Test]
	public void EqualityOperator() {
		var sut = new StreamRevision(1);
		Assert.True(new StreamRevision(1) == sut);
	}

	[Test]
	public void InequalityOperator() {
		var sut = new StreamRevision(1);
		Assert.True(new StreamRevision(2) != sut);
	}

	[Test]
	public void AdditionOperator() {
		var sut = StreamRevision.Start;
		Assert.AreEqual(new StreamRevision(1), sut + 1);
		Assert.AreEqual(new StreamRevision(1), 1 + sut);
	}

	public static IEnumerable<object[]> AdditionOutOfBoundsCases() {
		yield return new object[] {StreamRevision.End, 1UL};
		yield return new object[] {new StreamRevision(long.MaxValue), long.MaxValue + 2UL};
	}

	[TestCaseSource(nameof(AdditionOutOfBoundsCases))]
	public void AdditionOutOfBoundsThrows(StreamRevision streamRevision, ulong operand) {
		Assert.Throws<OverflowException>(() => {
			var _ = streamRevision + operand;
		});
		Assert.Throws<OverflowException>(() => {
			var _ = operand + streamRevision;
		});
	}

	[Test]
	public void SubtractionOperator() {
		var sut = new StreamRevision(1);
		Assert.AreEqual(new StreamRevision(0), sut - 1);
		Assert.AreEqual(new StreamRevision(0), 1 - sut);
	}

	public static IEnumerable<object[]> SubtractionOutOfBoundsCases() {
		yield return new object[] {new StreamRevision(1), 2UL};
		yield return new object[] {StreamRevision.Start, 1UL};
	}

	[TestCaseSource(nameof(SubtractionOutOfBoundsCases))]
	public void SubtractionOutOfBoundsThrows(StreamRevision streamRevision, ulong operand) {
		Assert.Throws<OverflowException>(() => {
			var _ = streamRevision - operand;
		});
		Assert.Throws<OverflowException>(() => {
			var _ = (ulong)streamRevision - new StreamRevision(operand);
		});
	}

	public static IEnumerable<object[]> ArgumentOutOfRangeTestCases() {
		yield return new object[] {long.MaxValue + 1UL};
		yield return new object[] {ulong.MaxValue - 1UL};
	}

	[TestCaseSource(nameof(ArgumentOutOfRangeTestCases))]
	public void ArgumentOutOfRange(ulong value) {
		var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new StreamRevision(value));
		Assert.AreEqual(nameof(value), ex.ParamName);
	}

	public static IEnumerable<object[]> ComparableTestCases() {
		yield return new object[] {StreamRevision.Start, StreamRevision.Start, 0};
		yield return new object[] {StreamRevision.Start, StreamRevision.End, -1};
		yield return new object[] {StreamRevision.End, StreamRevision.Start, 1};
	}

	[TestCaseSource(nameof(ComparableTestCases))]
	public void Comparability(StreamRevision left, StreamRevision right, int expected)
		=> Assert.AreEqual(expected, left.CompareTo(right));

	public static IEnumerable<object[]> Int64TestCases() {
		yield return new object[] {-1L, StreamRevision.End};
		yield return new object[] {0L, StreamRevision.Start};
	}

	[TestCaseSource(nameof(Int64TestCases))]
	public void FromInt64ExpectedResult(long value, StreamRevision expected)
		=> Assert.AreEqual(expected, StreamRevision.FromInt64(value));

	[TestCaseSource(nameof(Int64TestCases))]
	public void ToInt64ExpectedResult(long expected, StreamRevision value)
		=> Assert.AreEqual(expected, value.ToInt64());

	[Test]
	public void ExplicitConversionExpectedResult() {
		const ulong expected = 0UL;
		var actual = (ulong)new StreamRevision(expected);
		Assert.AreEqual(expected, actual);
	}

	[Test]
	public void ImplicitConversionExpectedResult() {
		const ulong expected = 0UL;
		ulong actual = new StreamRevision(expected);
		Assert.AreEqual(expected, actual);
	}

	[Test]
	public void ToStringExpectedResult() {
		var expected = 0UL.ToString();

		Assert.AreEqual(expected, new StreamRevision(0UL).ToString());
	}

	[Test]
	public void ToUInt64ExpectedResult() {
		var expected = 0UL;

		Assert.AreEqual(expected, new StreamRevision(expected).ToUInt64());
	}
}
