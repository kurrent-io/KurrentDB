// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace KurrentDB.Components.Cluster;

// Helpers for steering leader-only actions to the leader node when the UI is talking to a follower.
public static class LeaderRedirect {
	// Absolute URL to `path` on the leader — the current request's scheme with the leader's advertised
	// host:port — or null when there's no known leader.
	public static string PageUrl(NavigationManager nav, NodeHttpEndpoint? leader, string path) =>
		leader is { } e ? $"{new Uri(nav.BaseUri).Scheme}://{e.Ip}:{e.Port}{path}" : null;

	// If this node is a known follower, explain that `action` is leader-only and — when a leader is known —
	// offer to open `path` on the leader, navigating there on confirmation. Returns true if it intercepted,
	// so the caller should stop. Returns false on the leader or before the role is known, so the caller
	// proceeds (the service's requireLeader check is the backstop for that brief window).
	public static async Task<bool> InterceptNonLeader(
		GossipMonitor gossip, IDialogService dialogs, NavigationManager nav, string path, string action) {

		if (gossip.CurrentState is not { } state || state == VNodeState.Leader)
			return false;

		var url = PageUrl(nav, gossip.LeaderEndpoint, path);
		if (url is null) {
			await dialogs.ShowMessageBox("Leader required",
				$"{action} can only be done on the leader node, and no leader is available right now. Try again shortly.");
			return true;
		}

		var open = await dialogs.ShowMessageBox("Leader required",
			$"{action} can only be done on the leader node. Open this page on the leader to continue?",
			yesText: "Open on the leader", cancelText: "Cancel");
		if (open == true)
			nav.NavigateTo(url, forceLoad: true);
		return true;
	}
}
