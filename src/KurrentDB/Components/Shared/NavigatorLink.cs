// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Components.Shared;

// Navigator registers `kurrentdb` and `kurrentdb+discover` as OS protocol handlers (see its
// electron-builder-config.cjs), so handing the browser a connection string in one of those schemes launches
// the installed app pointed at this node. Mirrors the Cloud console's connect modal (bespin
// ui/src/components/modals/connect-modal/utils/openNavigator.ts).
static class NavigatorLink {
	const string Download = "https://navigator.kurrent.io/";

	public static string Fallback =>
		$"{Download}?utm_source=embedded-ui&utm_medium=referral&utm_campaign=tools&utm_content=sidebar";

	// Built from the address the browser reached this node on, not the node's own advertised address, which
	// can be cluster-internal and unreachable from the client. `+discover` only for a real cluster, so a
	// single node doesn't send Navigator through gossip discovery for one address it already has.
	//
	// Carries no credentials, unlike the Cloud console, which can assume its own default admin password:
	// this UI never sees the signed-in user's password, and a connection string is not the place for one.
	// Navigator prompts instead.
	public static string DeepLink(string baseUri, int memberCount) {
		var uri = new Uri(baseUri);
		var scheme = memberCount > 1 ? "kurrentdb+discover" : "kurrentdb";
		var tls = uri.Scheme == Uri.UriSchemeHttps ? "" : "?tls=false";
		return $"{scheme}://{uri.Host}:{uri.Port}{tls}";
	}
}
