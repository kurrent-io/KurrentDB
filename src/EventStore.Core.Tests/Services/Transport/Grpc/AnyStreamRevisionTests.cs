// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Services.Transport.Grpc;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Grpc;

[TestFixture]
public class AnyStreamRevisionTests {
	[Test]
	public void Equality() {
		var sut = AnyStreamRevision.NoStream;
		Assert.AreEqual(AnyStreamRevision.NoStream, sut);
	}

	[Test]
	public void Inequality() {
		var sut = AnyStreamRevision.NoStream;
		Assert.AreNotEqual(AnyStreamRevision.Any, sut);
	}

	[Test]
	public void EqualityOperator() {
		var sut = AnyStreamRevision.NoStream;
		Assert.True(AnyStreamRevision.NoStream == sut);
	}

	[Test]
	public void InequalityOperator() {
		var sut = AnyStreamRevision.NoStream;
		Assert.True(AnyStreamRevision.Any != sut);
	}

	public static IEnumerable<object[]> ArgumentOutOfRangeTestCases() {
		yield return new object[] { 0 };
		yield return new object[] { int.MaxValue };
		yield return new object[] { -3 };
	}

	[TestCaseSource(nameof(ArgumentOutOfRangeTestCases))]
	public void ArgumentOutOfRange(int value) {
		var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new AnyStreamRevision(value));
		Assert.AreEqual(nameof(value), ex.ParamName);
	}


	public static IEnumerable<object[]> Int64TestCases() {
		yield return new object[] { -1L, AnyStreamRevision.NoStream };
		yield return new object[] { -2L, AnyStreamRevision.Any };
		yield return new object[] { -4L, AnyStreamRevision.StreamExists };
	}

	[TestCaseSource(nameof(Int64TestCases))]
	public void FromInt64ExpectedResult(long value, AnyStreamRevision expected)
		=> Assert.AreEqual(expected, AnyStreamRevision.FromInt64(value));

	[TestCaseSource(nameof(Int64TestCases))]
	public void ToInt64ExpectedResult(long expected, AnyStreamRevision value)
		=> Assert.AreEqual(expected, value.ToInt64());

	[Test]
	public void ExplicitConversionExpectedResult() {
		const int expected = 1;
		var actual = (int)new AnyStreamRevision(expected);
		Assert.AreEqual(expected, actual);
	}

	[Test]
	public void ImplicitConversionExpectedResult() {
		const int expected = 1;
		int actual = new AnyStreamRevision(expected);
		Assert.AreEqual(expected, actual);
	}

	public static IEnumerable<object[]> ToStringTestCases() {
		yield return new object[] { AnyStreamRevision.Any, nameof(AnyStreamRevision.Any) };
		yield return new object[] { AnyStreamRevision.NoStream, nameof(AnyStreamRevision.NoStream) };
		yield return new object[] { AnyStreamRevision.StreamExists, nameof(AnyStreamRevision.StreamExists) };
	}

	[TestCaseSource(nameof(ToStringTestCases))]
	public void ToStringExpectedResult(AnyStreamRevision sut, string expected) {
		Assert.AreEqual(expected, sut.ToString());
	}
}
