// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace KurrentDB.Components.ServerInfo;

public partial class Preferences : ComponentBase {
	[Inject] ServerInfoService ServerInfoService { get; set; } = null!;
	[Inject] ISnackbar Snackbar { get; set; } = null!;
	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }

	string _name = "";
	bool _production;
	bool _loading = true;
	bool _saving;
	ClaimsPrincipal _principal;

	protected override async Task OnInitializedAsync() {
		var authState = await AuthenticationState;
		_principal = authState.User;
		await Load();
	}

	async Task Load() {
		_loading = true;
		try {
			using var cts = new CancellationTokenSource(5000);
			var info = await ServerInfoService.GetAsync(_principal, cts.Token);
			_name = info.Name ?? "";
			_production = info.Production;
		} catch (Exception ex) {
			Snackbar.Add($"Failed to load database info: {ex.Message}", Severity.Error);
		} finally {
			_loading = false;
		}
	}

	async Task Save() {
		_saving = true;
		try {
			using var cts = new CancellationTokenSource(10000);
			await ServerInfoService.UpdateAsync(_principal, new ServerInfoData {
				Name = _name,
				Production = _production
			}, cts.Token);
			Snackbar.Add("Database info updated.", Severity.Success);
		} catch (ServerInfoException ex) {
			Snackbar.Add(ex.Message, Severity.Error);
		} finally {
			_saving = false;
		}
	}
}
