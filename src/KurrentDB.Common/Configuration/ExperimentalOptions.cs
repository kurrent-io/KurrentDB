// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Configuration;

namespace KurrentDB.Common.Configuration;

public class ExperimentalOptions : IConfigurationBinder<ExperimentalOptions> {
	public bool AsyncIO { get; set; }

	static ExperimentalOptions IConfigurationBinder<ExperimentalOptions>.Bind(IConfiguration configuration)
		=> configuration.Get<ExperimentalOptions>()!;
}
