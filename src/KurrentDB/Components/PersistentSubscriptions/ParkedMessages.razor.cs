// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Components.Shared;
using KurrentDB.Core.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace KurrentDB.Components.PersistentSubscriptions;

public partial class ParkedMessages : ComponentBase {
	[Inject] PersistentSubscriptionsService PersistentSubscriptionsService { get; set; } = null!;
	[Inject] ISnackbar Snackbar { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }
	[Parameter] public string StreamId { get; set; }
	[Parameter] public string GroupName { get; set; }

	readonly StreamPageCursor _cursor = new(20);
	IReadOnlyList<ResolvedEvent> _events = [];
	bool _loading = true;
	ClaimsPrincipal _principal;

	protected override async Task OnInitializedAsync() {
		var authState = await AuthenticationState;
		_principal = authState.User;
		StreamId = Uri.UnescapeDataString(StreamId);
		GroupName = Uri.UnescapeDataString(GroupName);
		await LoadPage();
	}

	async Task LoadPage() {
		_loading = true;
		try {
			using var cts = new CancellationTokenSource(5000);
			var (events, isEnd, _) = await PersistentSubscriptionsService.GetParkedMessagesAsync(
				_principal, StreamId, GroupName, _cursor.From, _cursor.PageSize, cts.Token);
			_events = events;
			_cursor.OnLoaded(
				events.Count > 0 ? events[0].OriginalEventNumber : null,
				events.Count > 0 ? events[^1].OriginalEventNumber : null,
				isEnd);
		} catch (PersistentSubscriptionsException ex) {
			Snackbar.Add(ex.Message, Severity.Error);
		} catch (Exception) {
			// Transient
		} finally {
			_loading = false;
		}
	}

	async Task NewestPage() { _cursor.ToNewest(); await LoadPage(); }
	async Task OldestPage() { _cursor.ToOldest(); await LoadPage(); }
	async Task NextPage() { _cursor.ToOlder(); await LoadPage(); }      // older
	async Task PreviousPage() { _cursor.ToNewer(); await LoadPage(); }  // newer

	async Task OnPageSizeChanged(int value) {
		_cursor.ChangePageSize(value);
		await LoadPage();
	}
}
