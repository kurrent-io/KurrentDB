// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests;

[SetUpFixture]
public class TestsInitFixture {
	private readonly KurrentDB.Core.Tests.TestsInitFixture _initFixture =
		new KurrentDB.Core.Tests.TestsInitFixture();

	[OneTimeSetUp]
	public void SetUp() {
		_initFixture.SetUp();
	}

	[OneTimeTearDown]
	public void TearDown() {
		_initFixture.TearDown();
	}
}
