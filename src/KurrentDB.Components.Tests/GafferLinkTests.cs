// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Components.Shared;
using Xunit;

namespace KurrentDB.Components.Tests;

public class GafferLinkTests {
	[Fact]
	public void Carries_the_shared_attribution_and_the_placement() {
		var url = GafferLink.For("projections_list");

		Assert.StartsWith("https://gaffer.kurrent.io/?", url);
		Assert.Contains("utm_source=embedded-ui", url);
		Assert.Contains("utm_medium=referral", url);
		Assert.Contains("utm_campaign=projections", url);   // the default
		Assert.Contains("utm_content=projections_list", url);
	}

	[Fact]
	public void Campaign_can_be_overridden_for_a_surface_off_the_projections_pages() =>
		Assert.Contains("utm_campaign=tools&utm_content=sidebar", GafferLink.For("sidebar", campaign: "tools"));

	// Reserved characters would otherwise split the query string and truncate the attribution silently.
	[Fact]
	public void Reserved_characters_are_escaped_rather_than_ending_the_parameter() {
		var url = GafferLink.For("a&b=c d", campaign: "x&y");

		Assert.Contains("utm_campaign=x%26y", url);
		Assert.Contains("utm_content=a%26b%3Dc%20d", url);
		Assert.EndsWith("utm_content=a%26b%3Dc%20d", url);   // nothing leaked into a new parameter
	}
}
