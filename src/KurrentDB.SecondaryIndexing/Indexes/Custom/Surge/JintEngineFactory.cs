// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using Jint;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Surge;

static class JintEngineFactory {
	static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

	public static Engine CreateEngine(TimeSpan? executionTimeout = null) {
		var timeout = executionTimeout ?? DefaultTimeout;

		return new Engine(options => {
			options
				.Strict()
				.Culture(CultureInfo.InvariantCulture)
				.DisableStringCompilation()
				.TimeoutInterval(timeout);
		});
	}
}
