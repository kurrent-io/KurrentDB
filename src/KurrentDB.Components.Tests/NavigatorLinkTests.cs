// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Components.Shared;
using Xunit;

namespace KurrentDB.Components.Tests;

// The sidebar hands this string to the OS to launch Navigator, so a wrong scheme or a missing tls flag
// surfaces as "the app opened and then failed to connect", with nothing in this UI to explain why.
public class NavigatorLinkTests {
	[Fact]
	public void Single_node_uses_the_plain_scheme() =>
		Assert.Equal("kurrentdb://db.example.com:2113",
			NavigatorLink.DeepLink("https://db.example.com:2113/", memberCount: 1));

	// Gossip hasn't reported yet: treat it as a single node rather than sending Navigator through discovery.
	[Fact]
	public void No_known_members_uses_the_plain_scheme() =>
		Assert.Equal("kurrentdb://db.example.com:2113",
			NavigatorLink.DeepLink("https://db.example.com:2113/", memberCount: 0));

	[Fact]
	public void Cluster_uses_the_discover_scheme() =>
		Assert.Equal("kurrentdb+discover://db.example.com:2113",
			NavigatorLink.DeepLink("https://db.example.com:2113/", memberCount: 3));

	// An http node is running insecure; without this Navigator would attempt TLS and fail to connect.
	[Fact]
	public void Insecure_node_carries_tls_false() =>
		Assert.Equal("kurrentdb://localhost:2113?tls=false",
			NavigatorLink.DeepLink("http://localhost:2113/", memberCount: 1));

	[Fact]
	public void Insecure_cluster_carries_tls_false() =>
		Assert.Equal("kurrentdb+discover://localhost:2113?tls=false",
			NavigatorLink.DeepLink("http://localhost:2113/", memberCount: 2));

	// The port is always explicit, including when it is the scheme default, so Navigator never has to guess.
	[Fact]
	public void Default_port_is_still_explicit() =>
		Assert.Equal("kurrentdb://db.example.com:443",
			NavigatorLink.DeepLink("https://db.example.com/", memberCount: 1));

	[Fact]
	public void Fallback_is_the_download_page_with_attribution() {
		Assert.StartsWith("https://navigator.kurrent.io/", NavigatorLink.Fallback);
		Assert.Contains("utm_source=embedded-ui", NavigatorLink.Fallback);
	}
}
