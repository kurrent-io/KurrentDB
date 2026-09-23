// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Configuration;

namespace KurrentDB.Common.Configuration;

public interface IConfigurationBinder<out TSelf>
	where TSelf : class, new() {
	public static abstract TSelf Bind(IConfiguration configuration);
}
