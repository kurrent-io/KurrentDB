// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace EventStore.Core.Tests;

public class SpecificationWithFilePerTestFixture {
	protected string Filename;

	[OneTimeSetUp]
	public virtual Task TestFixtureSetUp() {
		var typeName = GetType().Name.Length > 30 ? GetType().Name.Substring(0, 30) : GetType().Name;
		Filename = Path.Combine(Path.GetTempPath(), string.Format("ES-{0}-{1}", Guid.NewGuid(), typeName));
		return Task.CompletedTask;
	}

	[OneTimeTearDown]
	public virtual void TestFixtureTearDown() {
		if (File.Exists(Filename))
			File.Delete(Filename);
	}
}
