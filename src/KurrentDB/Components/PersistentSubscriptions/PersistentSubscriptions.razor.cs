// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Components.Cluster;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace KurrentDB.Components.PersistentSubscriptions;

public sealed partial class PersistentSubscriptions : ComponentBase, IDisposable {
	[Inject] PersistentSubscriptionsService PersistentSubscriptionsService { get; set; } = null!;
	[Inject] GossipMonitor Gossip { get; set; } = null!;
	[Inject] IDialogService DialogService { get; set; } = null!;
	[Inject] ISnackbar Snackbar { get; set; } = null!;
	[Inject] NavigationManager Navigation { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }

	List<MonitoringMessage.PersistentSubscriptionInfo> _subscriptions = [];
	bool _loading = true;

	// Persistent subscriptions only run on the leader, so on a follower this page has nothing to manage — we
	// show a notice and a link to the leader's equivalent page instead. The role comes live from
	// GossipMonitor; the leader's URL needs a gossip read (to discover its endpoint).
	string _leaderUrl;
	bool IsLeader => Gossip.CurrentState == VNodeState.Leader;
	// Only show the "go to the leader" notice once we positively know this node isn't the leader and we have a
	// leader to link to — so it never flashes on the leader, during startup, or mid-election.
	bool ShowLeaderNotice => Gossip.CurrentState is { } s && s != VNodeState.Leader && _leaderUrl is not null;
	// Friendly label for this node's role, shown in the "go to the leader" notice.
	string RoleLabel => Gossip.CurrentState is { } s ? NodeStateDisplay.Label(s) : "";

	// Per-column aggregates shown in each stream group's footer (and the grand total).
	// Sums for throughput/counts; minimums for event positions (the header reflects the
	// furthest-behind group, matching the legacy Angular SubscriptionsMapper).
	readonly AggregateDefinition<MonitoringMessage.PersistentSubscriptionInfo> _avgPerSecondAggregate = new() {
		Type = AggregateType.Custom,
		CustomAggregate = items => items.Sum(x => x.AveragePerSecond).ToString("N0")
	};
	readonly AggregateDefinition<MonitoringMessage.PersistentSubscriptionInfo> _lastKnownAggregate = new() {
		Type = AggregateType.Custom,
		CustomAggregate = items => MinPosition(items.Select(x => x.LastKnownEventPosition))
	};
	readonly AggregateDefinition<MonitoringMessage.PersistentSubscriptionInfo> _checkpointAggregate = new() {
		Type = AggregateType.Custom,
		CustomAggregate = items => MinPosition(items.Select(x => x.LastCheckpointedEventPosition))
	};
	readonly AggregateDefinition<MonitoringMessage.PersistentSubscriptionInfo> _inFlightAggregate = new() {
		Type = AggregateType.Custom,
		CustomAggregate = items => items.Sum(x => x.TotalInFlightMessages).ToString("N0")
	};
	readonly AggregateDefinition<MonitoringMessage.PersistentSubscriptionInfo> _connectionsAggregate = new() {
		Type = AggregateType.Custom,
		CustomAggregate = items => items.Sum(x => x.Connections?.Count ?? 0).ToString("N0")
	};
	readonly AggregateDefinition<MonitoringMessage.PersistentSubscriptionInfo> _behindAggregate = new() {
		Type = AggregateType.Custom,
		CustomAggregate = items => {
			var list = items.ToList();
			if (list.Count == 0 || list.Any(x => x.EventSource == "$all"))
				return "";
			var messages = list.Sum(BehindByMessages);
			var seconds = Math.Round(list.Sum(BehindByTime), 2);
			return $"{messages} / {seconds}";
		}
	};
	readonly AggregateDefinition<MonitoringMessage.PersistentSubscriptionInfo> _totalItemsAggregate = new() {
		Type = AggregateType.Custom,
		CustomAggregate = items => items.Sum(x => x.TotalItems).ToString("N0")
	};
	readonly AggregateDefinition<MonitoringMessage.PersistentSubscriptionInfo> _parkedAggregate = new() {
		Type = AggregateType.Custom,
		CustomAggregate = items => items.Sum(x => x.ParkedMessageCount).ToString("N0")
	};

	// "Behind" status: how far the subscription's checkpoint trails the last known event,
	// shown as "<messages> / <estimated seconds to catch up>". Blank for $all (no meaningful
	// single position). Mirrors the legacy Angular behindByMessages/behindByTime computation.
	static long BehindByMessages(MonitoringMessage.PersistentSubscriptionInfo x) {
		if (x.EventSource == "$all" || string.IsNullOrEmpty(x.LastKnownEventPosition))
			return 0;
		return ParseLong(x.LastKnownEventPosition) - ParseLong(x.LastCheckpointedEventPosition) + 1;
	}

	static double BehindByTime(MonitoringMessage.PersistentSubscriptionInfo x) {
		if (x.AveragePerSecond <= 0)
			return 0;
		var seconds = Math.Round((double)BehindByMessages(x) / x.AveragePerSecond, 2);
		return double.IsFinite(seconds) ? seconds : 0;
	}

	static string BehindStatus(MonitoringMessage.PersistentSubscriptionInfo x) =>
		x.EventSource == "$all" ? "" : $"{BehindByMessages(x)} / {BehindByTime(x)}";

	// Positions are event numbers for regular streams and "C:<commit>/P:<prepare>" for $all.
	// We rank by the larger of commit/prepare so the minimum is the furthest-behind group.
	static string MinPosition(IEnumerable<string> positions) {
		string min = null;
		var minRank = long.MaxValue;
		foreach (var p in positions) {
			if (string.IsNullOrEmpty(p))
				continue;
			var rank = PositionRank(p);
			if (min == null || rank < minRank) {
				min = p;
				minRank = rank;
			}
		}
		return min ?? "";
	}

	static long PositionRank(string pos) {
		if (string.IsNullOrEmpty(pos))
			return long.MaxValue;
		if (pos.Contains("C:")) {
			var parts = pos.Split('/');
			var commit = ParseAfter(parts[0], "C:");
			var prepare = parts.Length > 1 ? ParseAfter(parts[1], "P:") : commit;
			return Math.Max(commit, prepare);
		}
		return ParseLong(pos);
	}

	static long ParseAfter(string s, string prefix) {
		var idx = s.IndexOf(prefix, StringComparison.Ordinal);
		return idx < 0 ? 0 : ParseLong(s[(idx + prefix.Length)..]);
	}

	static long ParseLong(string s) => long.TryParse(s, out var v) ? v : 0;

	Timer _timer;
	ClaimsPrincipal _principal;

	int _page;
	int _pageSize = 20;
	int _total;

	protected override async Task OnInitializedAsync() {
		var authState = await AuthenticationState;
		_principal = authState.User;
		// React promptly to a failover instead of waiting for the next poll.
		Gossip.Changed += OnGossipChanged;
		await Refresh();
		_timer = new(Callback, null, 5000, 5000);
	}

	void OnGossipChanged() => Callback(null);

	void Callback(object state) {
		Task.Run(async () => {
			await Refresh();
			try {
				await InvokeAsync(StateHasChanged);
			} catch (ObjectDisposedException) {
				// Component/renderer torn down between ticks — ignore.
			}
		});
	}

	async Task Refresh() {
		// Reflect the current leader (from gossip, via the monitor) so a failover updates the link.
		UpdateLeaderUrl();
		if (ShowLeaderNotice) {
			_loading = false;
			return;
		}
		try {
			using var cts = new CancellationTokenSource(5000);
			var (items, total) = await PersistentSubscriptionsService.GetPageAsync(_principal, _page * _pageSize, _pageSize, cts.Token);
			_subscriptions = items;
			_total = total;
		} catch (Exception) {
			// Keep stale data
		} finally {
			_loading = false;
		}
	}

	// When this node isn't the leader, point at the leader's equivalent page (its endpoint comes from gossip).
	void UpdateLeaderUrl() {
		_leaderUrl = !IsLeader && Gossip.LeaderEndpoint is { } e
			? $"{new Uri(Navigation.BaseUri).Scheme}://{e.Ip}:{e.Port}/ui/persistent-subscriptions"
			: null;
	}

	async Task NextPage() {
		if ((_page + 1) * _pageSize < _total) {
			_page++;
			await Refresh();
		}
	}

	async Task PreviousPage() {
		if (_page > 0) {
			_page--;
			await Refresh();
		}
	}

	async Task FirstPage() {
		if (_page > 0) {
			_page = 0;
			await Refresh();
		}
	}

	async Task OnPageSizeChanged(int value) {
		if (value < 1)
			value = 1;
		_pageSize = value;
		_page = 0;
		await Refresh();
	}

	async Task RestartSubsystem() {
		var confirmed = await DialogService.ShowMessageBox(
			"Restart Subsystem",
			"Restart the persistent subscriptions subsystem?",
			yesText: "Restart", cancelText: "Cancel");
		if (confirmed != true)
			return;
		try {
			using var cts = new CancellationTokenSource(30000);
			await PersistentSubscriptionsService.RestartSubsystemAsync(_principal, cts.Token);
			Snackbar.Add("Persistent subscriptions subsystem restarting.", Severity.Success);
			await Refresh();
		} catch (PersistentSubscriptionsException ex) {
			Snackbar.Add(ex.Message, Severity.Error);
		}
	}

	async Task CreateSubscription() {
		var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseOnEscapeKey = true };
		var dialog = await DialogService.ShowAsync<CreatePersistentSubscriptionDialog>("New Subscription", options);
		var result = await dialog.Result;
		if (result is { Canceled: false, Data: CreatePersistentSubscriptionDialog.CreateSubscriptionModel model }) {
			Snackbar.Add($"Subscription '{model.GroupName}' created.", Severity.Success);
			await Refresh();
		}
	}

	public void Dispose() {
		Gossip.Changed -= OnGossipChanged;
		_timer?.Dispose();
	}
}
