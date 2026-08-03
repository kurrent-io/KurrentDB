// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Components.Shared;

// UTM-tagged links to Gaffer. `campaign` is the surface the link sits on, `content` the placement within it.
// source/medium and the `projections` campaign match what Navigator sends, so the two admin surfaces
// aggregate as one referral channel rather than looking like separate acquisition channels.
static class GafferLink {
	const string Home = "https://gaffer.kurrent.io/";

	public static string For(string content, string campaign = "projections") =>
		$"{Home}?utm_source=embedded-ui&utm_medium=referral&utm_campaign={campaign}&utm_content={content}";
}
