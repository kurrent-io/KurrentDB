// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.DataStructures.ProbabilisticFilter;
using NUnit.Framework;

namespace EventStore.Core.Tests.DataStructures;

[TestFixture]
public class byte_extensions_should {
	[DatapointSource]
	public static object[][] IsBitSetCases = new object[][] {
		new object[] { true, (byte)0b1111_0000, 0 },
		new object[] { true, (byte)0b1111_0000, 1 },
		new object[] { true, (byte)0b1111_0000, 2 },
		new object[] { true, (byte)0b1111_0000, 3 },
		new object[] { false, (byte)0b1111_0000, 4 },
		new object[] { false, (byte)0b1111_0000, 5 },
		new object[] { false, (byte)0b1111_0000, 6 },
		new object[] { false, (byte)0b1111_0000, 7 },
		new object[] { false, (byte)0b0000_1111, 0 },
		new object[] { false, (byte)0b0000_1111, 1 },
		new object[] { false, (byte)0b0000_1111, 2 },
		new object[] { false, (byte)0b0000_1111, 3 },
		new object[] { true, (byte)0b0000_1111, 4 },
		new object[] { true, (byte)0b0000_1111, 5 },
		new object[] { true, (byte)0b0000_1111, 6 },
		new object[] { true, (byte)0b0000_1111, 7 },
	};

	[TestCaseSource(nameof(IsBitSetCases))]
	public void check_bit_correctly(bool expected, byte x, int bitIndex) {
		Assert.AreEqual(expected, x.IsBitSet(bitIndex));
	}

	[DatapointSource]
	public static object[][] SetBitCases = new object[][] {
		new object[] { (byte)0b1111_0000, (byte)0b1111_0000, 0 },
		new object[] { (byte)0b1111_0000, (byte)0b1111_0000, 1 },
		new object[] { (byte)0b1111_0000, (byte)0b1111_0000, 2 },
		new object[] { (byte)0b1111_0000, (byte)0b1111_0000, 3 },
		new object[] { (byte)0b1111_1000, (byte)0b1111_0000, 4 },
		new object[] { (byte)0b1111_0100, (byte)0b1111_0000, 5 },
		new object[] { (byte)0b1111_0010, (byte)0b1111_0000, 6 },
		new object[] { (byte)0b1111_0001, (byte)0b1111_0000, 7 },
		new object[] { (byte)0b1000_1111, (byte)0b0000_1111, 0 },
		new object[] { (byte)0b0100_1111, (byte)0b0000_1111, 1 },
		new object[] { (byte)0b0010_1111, (byte)0b0000_1111, 2 },
		new object[] { (byte)0b0001_1111, (byte)0b0000_1111, 3 },
		new object[] { (byte)0b0000_1111, (byte)0b0000_1111, 4 },
		new object[] { (byte)0b0000_1111, (byte)0b0000_1111, 5 },
		new object[] { (byte)0b0000_1111, (byte)0b0000_1111, 6 },
		new object[] { (byte)0b0000_1111, (byte)0b0000_1111, 7 },
	};

	[TestCaseSource(nameof(SetBitCases))]
	public void set_bit_correctly(byte expected, byte x, int bitIndex) {
		Assert.AreEqual(expected, x.SetBit(bitIndex));
	}

	[DatapointSource]
	public static object[][] UnsetBitCases = new object[][] {
		new object[] { (byte)0b0111_0000, (byte)0b1111_0000, 0 },
		new object[] { (byte)0b1011_0000, (byte)0b1111_0000, 1 },
		new object[] { (byte)0b1101_0000, (byte)0b1111_0000, 2 },
		new object[] { (byte)0b1110_0000, (byte)0b1111_0000, 3 },
		new object[] { (byte)0b1111_0000, (byte)0b1111_0000, 4 },
		new object[] { (byte)0b1111_0000, (byte)0b1111_0000, 5 },
		new object[] { (byte)0b1111_0000, (byte)0b1111_0000, 6 },
		new object[] { (byte)0b1111_0000, (byte)0b1111_0000, 7 },
		new object[] { (byte)0b0000_1111, (byte)0b0000_1111, 0 },
		new object[] { (byte)0b0000_1111, (byte)0b0000_1111, 1 },
		new object[] { (byte)0b0000_1111, (byte)0b0000_1111, 2 },
		new object[] { (byte)0b0000_1111, (byte)0b0000_1111, 3 },
		new object[] { (byte)0b0000_0111, (byte)0b0000_1111, 4 },
		new object[] { (byte)0b0000_1011, (byte)0b0000_1111, 5 },
		new object[] { (byte)0b0000_1101, (byte)0b0000_1111, 6 },
		new object[] { (byte)0b0000_1110, (byte)0b0000_1111, 7 },
	};

	[TestCaseSource(nameof(UnsetBitCases))]
	public void unset_bit_correctly(byte expected, byte x, int bitIndex) {
		Assert.AreEqual(expected, x.UnsetBit(bitIndex));
	}
}
