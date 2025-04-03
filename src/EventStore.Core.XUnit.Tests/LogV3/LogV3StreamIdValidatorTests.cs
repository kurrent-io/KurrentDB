// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.LogV3;
using Xunit;

namespace EventStore.Core.XUnit.Tests.LogV3;

public class LogV3StreamIdValidatorTests {
	readonly LogV3StreamIdValidator _sut = new();

	[Fact]
	public void accepts_positive() {
		_sut.Validate(1);
	}

	[Fact]
	public void accepts_zero() {
		_sut.Validate(0);
	}
}
