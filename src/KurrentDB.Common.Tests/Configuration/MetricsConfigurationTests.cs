// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Configuration;
using Microsoft.Extensions.Configuration;

namespace KurrentDB.Common.Tests.Configuration;

public class MetricsConfigurationTests {
	[Fact]
	public void Configuration() {
		var config = new ConfigurationBuilder()
			.AddSection("KurrentDB:Metrics:SlowMessageMilliseconds", b =>
				b.AddInMemoryCollection([
					new("bus30", "30"),
					new("bus0", "0"),
					new("bus-1", "-1"),
				]))
			.Build();
		var sut = MetricsConfiguration.Get(config);

		Assert.Equal(30, sut.GetBusSlowMessageThreshold("bus30").TotalMilliseconds);
		Assert.Equal(0, sut.GetBusSlowMessageThreshold("bus0").TotalMilliseconds);
		Assert.Equal(0, sut.GetBusSlowMessageThreshold("bus-1").TotalMilliseconds);
		Assert.Equal(ConfigConstants.DefaultSlowMessageThreshold, sut.GetBusSlowMessageThreshold("unlisted bus"));

	}
}
