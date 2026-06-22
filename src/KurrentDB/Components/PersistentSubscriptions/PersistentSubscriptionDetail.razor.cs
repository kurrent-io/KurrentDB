// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
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

public sealed partial class PersistentSubscriptionDetail : ComponentBase, IDisposable {
	[Inject] PersistentSubscriptionsService PersistentSubscriptionsService { get; set; } = null!;
	[Inject] IDialogService DialogService { get; set; } = null!;
	[Inject] ISnackbar Snackbar { get; set; } = null!;
	[Inject] NavigationManager Navigation { get; set; } = null!;
	[Inject] GossipMonitor Gossip { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }
	[Parameter] public string StreamId { get; set; }
	[Parameter] public string GroupName { get; set; }

	MonitoringMessage.PersistentSubscriptionInfo _info;
	bool _loading = true;
	ClaimsPrincipal _principal;

	// Persistent subscriptions are managed on the leader, so the mutating actions are only offered there. On a
	// follower we hide them and link to this subscription on the leader (the details below still render here).
	bool IsLeader => Gossip.CurrentState == VNodeState.Leader;
	string LeaderUrl => Gossip.CurrentState is { } s && s != VNodeState.Leader && Gossip.LeaderEndpoint is { } e
		? $"{new Uri(Navigation.BaseUri).Scheme}://{e.Ip}:{e.Port}/ui/persistent-subscriptions/{Uri.EscapeDataString(StreamId)}/{Uri.EscapeDataString(GroupName)}"
		: null;

	protected override async Task OnInitializedAsync() {
		var authState = await AuthenticationState;
		_principal = authState.User;
		StreamId = Uri.UnescapeDataString(StreamId);
		GroupName = Uri.UnescapeDataString(GroupName);
		// Re-render promptly on a failover so the management actions appear/disappear with the role.
		Gossip.Changed += OnGossipChanged;
		await LoadDetail();
	}

	async void OnGossipChanged() {
		try {
			await InvokeAsync(StateHasChanged);
		} catch (ObjectDisposedException) {
			// Renderer torn down between the gossip change and this callback — ignore.
		}
	}

	public void Dispose() => Gossip.Changed -= OnGossipChanged;

	async Task LoadDetail() {
		try {
			using var cts = new CancellationTokenSource(5000);
			_info = await PersistentSubscriptionsService.GetDetailAsync(_principal, StreamId, GroupName, cts.Token);
		} catch (PersistentSubscriptionsException) {
			_info = null;
		} catch (Exception) {
			_info = null;
		} finally {
			_loading = false;
		}
	}

	async Task Edit() {
		if (_info == null)
			return;

		var config = new SubscriptionConfig {
			ResolveLinkTos = _info.ResolveLinktos,
			StartFrom = long.TryParse(_info.StartFrom, out var startFrom) ? startFrom : 0,
			MessageTimeoutMilliseconds = _info.MessageTimeoutMilliseconds,
			ExtraStatistics = _info.ExtraStatistics,
			MaxRetryCount = _info.MaxRetryCount,
			BufferSize = _info.BufferSize,
			LiveBufferSize = _info.LiveBufferSize,
			ReadBatchSize = _info.ReadBatchSize,
			CheckPointAfterMilliseconds = _info.CheckPointAfterMilliseconds,
			MinCheckPointCount = _info.MinCheckPointCount,
			MaxCheckPointCount = _info.MaxCheckPointCount,
			MaxSubscriberCount = _info.MaxSubscriberCount,
			NamedConsumerStrategy = _info.NamedConsumerStrategy,
		};

		var parameters = new DialogParameters<EditPersistentSubscriptionDialog> {
			{ x => x.StreamId, StreamId },
			{ x => x.GroupName, GroupName },
			{ x => x.Config, config },
		};
		var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseOnEscapeKey = true };
		var dialog = await DialogService.ShowAsync<EditPersistentSubscriptionDialog>("Edit Subscription", parameters, options);
		var result = await dialog.Result;
		if (result is { Canceled: false }) {
			Snackbar.Add("Subscription updated.", Severity.Success);
			await LoadDetail();
		}
	}

	async Task ReplayParked() {
		var confirmed = await DialogService.ShowMessageBox(
			"Replay Parked Messages",
			$"Are you sure you want to replay all parked messages for '{GroupName}' on '{StreamId}'?",
			yesText: "Replay", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				using var cts = new CancellationTokenSource(10000);
				await PersistentSubscriptionsService.ReplayParkedAsync(_principal, StreamId, GroupName, cts.Token);
				Snackbar.Add("Parked messages replay started.", Severity.Success);
				await LoadDetail();
			} catch (PersistentSubscriptionsException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}

	async Task Delete() {
		var confirmed = await DialogService.ShowMessageBox(
			"Delete Subscription",
			$"Are you sure you want to delete subscription '{GroupName}' on '{StreamId}'?",
			yesText: "Delete", cancelText: "Cancel");
		if (confirmed == true) {
			try {
				using var cts = new CancellationTokenSource(5000);
				await PersistentSubscriptionsService.DeleteAsync(_principal, StreamId, GroupName, cts.Token);
				Snackbar.Add("Subscription deleted.", Severity.Success);
				Navigation.NavigateTo("/ui/persistent-subscriptions");
			} catch (PersistentSubscriptionsException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}
}
