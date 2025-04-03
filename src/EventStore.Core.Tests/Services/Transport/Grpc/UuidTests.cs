// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Services.Transport.Grpc;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Grpc;

public class UuidTests {
	[Test]
	public void Equality() {
		var sut = Uuid.NewUuid();
		Assert.AreEqual(Uuid.FromGuid(sut.ToGuid()), sut);
	}

	[Test]
	public void Inequality() {
		var sut = Uuid.NewUuid();
		Assert.AreNotEqual(Uuid.NewUuid(), sut);
	}

	[Test]
	public void EqualityOperator() {
		var sut = Uuid.NewUuid();
		Assert.True(Uuid.FromGuid(sut.ToGuid()) == sut);
	}

	[Test]
	public void InequalityOperator() {
		var sut = Uuid.NewUuid();
		Assert.True(Uuid.NewUuid() != sut);
	}

	[Test]
	public void ArgumentNullException() {
		var ex = Assert.Throws<ArgumentNullException>(() => Uuid.Parse(null));
		Assert.AreEqual("value", ex.ParamName);
	}

	[Test]
	public void ToGuidReturnsExpectedResult() {
		var guid = Guid.NewGuid();
		var sut = Uuid.FromGuid(guid);

		Assert.AreEqual(sut.ToGuid(), guid);
	}

	[Test]
	public void ToStringProducesExpectedResult() {
		var sut = Uuid.NewUuid();

		Assert.AreEqual(sut.ToGuid().ToString(),sut.ToString());
	}

	[Test]
	public void ToFormattedStringProducesExpectedResult() {
		var sut = Uuid.NewUuid();

		Assert.AreEqual(sut.ToGuid().ToString("n"),sut.ToString("n"));
	}


	[Test]
	public void ToDtoReturnsExpectedResult() {
		var msb = GetRandomInt64();
		var lsb = GetRandomInt64();

		var sut = Uuid.FromInt64(msb, lsb);

		var result = sut.ToDto();

		Assert.NotNull(result.Structured);
		Assert.AreEqual(lsb, result.Structured.LeastSignificantBits);
		Assert.AreEqual(msb, result.Structured.MostSignificantBits);
	}

	[Test]
	public void ParseReturnsExpectedResult() {
		var guid = Guid.NewGuid();

		var sut = Uuid.Parse(guid.ToString());

		Assert.AreEqual(Uuid.FromGuid(guid), sut);
	}

	[Test]
	public void FromInt64ReturnsExpectedResult() {
		var guid = Guid.Parse("65678f9b-d139-4786-8305-b9166922b378");
		var sut = Uuid.FromInt64(7306966819824813958L, -9005588373953137800L);
		var expected = Uuid.FromGuid(guid);

		Assert.AreEqual(expected, sut);
	}

	private static long GetRandomInt64() {
		var buffer = new byte[sizeof(long)];

		new Random().NextBytes(buffer);

		return BitConverter.ToInt64(buffer, 0);
	}
}
