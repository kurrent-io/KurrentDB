// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests;

public class ConfigTests {
	[Test]
	public async Task KontextStorageConfig_DataPath_Defaults_Empty() {
		var config = new KontextStorageConfig();
		await Assert.That(config.DataPath).IsEqualTo("");
	}

	// -------- Core config validation --------

	[Test]
	public async Task Validate_Throws_On_Empty_DataPath() {
		var ex = Assert.Throws<ArgumentException>(() =>
			new KontextStorageConfig { DataPath = "" }.Validate());
		await Assert.That(ex.Message).Contains("DataPath");
	}
}