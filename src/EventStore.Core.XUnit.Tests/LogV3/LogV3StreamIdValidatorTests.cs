// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

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
