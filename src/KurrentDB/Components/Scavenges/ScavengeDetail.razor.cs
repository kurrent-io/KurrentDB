// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace KurrentDB.Components.Scavenges;

public partial class ScavengeDetail : ComponentBase {
	[Inject] ScavengeService ScavengeService { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }
	[Parameter] public string ScavengeId { get; set; }

	readonly StreamPageCursor _cursor = new(20);
	IReadOnlyList<ScavengeDetailEvent> _events = [];
	bool _loading = true;
	string _loadError;
	ClaimsPrincipal _principal;

	protected override async Task OnInitializedAsync() {
		_principal = (await AuthenticationState).User;
		await LoadPage();
	}

	async Task LoadPage() {
		_loading = true;
		_loadError = null;
		try {
			using var cts = new CancellationTokenSource(5000);
			var page = await ScavengeService.GetDetailPageAsync(
				_principal, ScavengeId, _cursor.From, _cursor.PageSize, cts.Token);
			_events = page.Events;
			_cursor.OnLoaded(page.FirstEventNumber, page.LastEventNumber, page.IsEndOfStream);
		} catch (ScavengeException ex) {
			_loadError = ex.Message;
		} catch (Exception) {
			// Distinguish a real failure from a genuinely empty scavenge (NoRecordsContent) so a
			// transient error isn't shown as "no events".
			_loadError = "Failed to load scavenge details. Please try again.";
		} finally {
			_loading = false;
		}
	}

	async Task NewestPage() { _cursor.ToNewest(); await LoadPage(); }
	async Task OldestPage() { _cursor.ToOldest(); await LoadPage(); }
	async Task OlderPage() { _cursor.ToOlder(); await LoadPage(); }
	async Task NewerPage() { _cursor.ToNewer(); await LoadPage(); }

	async Task OnPageSizeChanged(int value) {
		_cursor.ChangePageSize(value);
		await LoadPage();
	}
}
