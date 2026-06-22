// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Util;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace KurrentDB.Components.Users;

public sealed partial class Users : ComponentBase, IDisposable {
	[Inject] Core.ClusterVNodeOptions Configuration { get; set; } = null!;
	[Inject] UserManagementService UserService { get; set; } = null!;
	[Inject] IDialogService DialogService { get; set; } = null!;
	[Inject] ISnackbar Snackbar { get; set; } = null!;
	[Inject] NavigationManager Navigation { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }

	UserManagementMessage.UserData[] _users = [];
	bool _loading = true;
	string _loadError;
	Timer _timer;
	ClaimsPrincipal _principal;

	// The built-in users subsystem is only enabled for internal authentication in secure mode. It's off in
	// insecure mode and when an external provider (OAuth/LDAP) handles authentication — in those cases the
	// reads would just fail, so show a calm explanation instead of a red error and don't poll.
	bool CanManageUsers => !Configuration.Application.Insecure
		&& Configuration.Auth.AuthenticationType == Opts.AuthenticationTypeDefault;

	string UnavailableMessage => Configuration.Application.Insecure
		? "User management isn't available in insecure mode."
		: "Users are managed by your configured authentication provider.";

	protected override async Task OnInitializedAsync() {
		if (!CanManageUsers) {
			_loading = false;
			return;
		}

		var authState = await AuthenticationState;
		_principal = authState.User;
		await RefreshUsers();
		_timer = new(Callback, null, 5000, 5000);
	}

	void Callback(object state) {
		Task.Run(async () => {
			await RefreshUsers();
			try {
				await InvokeAsync(StateHasChanged);
			} catch (ObjectDisposedException) {
				// Component/renderer torn down between ticks — ignore.
			}
		});
	}

	async Task RefreshUsers() {
		try {
			using var cts = new CancellationTokenSource(5000);
			var data = await UserService.GetAllAsync(_principal, cts.Token);
			_users = data.OrderBy(u => u.LoginName).ToArray();
			_loadError = null;
		} catch (Exception ex) {
			// Keep showing stale data on transient refresh failures; only surface an error when there's
			// nothing to show (e.g. the initial load failed) so a blip doesn't replace good data or flicker.
			if (_users.Length == 0)
				_loadError = ex is UserManagementException ? ex.Message : "Failed to load users. Please try again.";
		} finally {
			_loading = false;
		}
	}

	async Task CreateUser() {
		var dialog = await DialogService.ShowAsync<CreateUserDialog>("Create User", UserDialogDefaults.Options);
		var result = await dialog.Result;
		if (result is { Canceled: false, Data: CreateUserDialog.CreateUserModel model }) {
			try {
				using var cts = new CancellationTokenSource(5000);
				await UserService.CreateAsync(_principal, model.LoginName, model.FullName, model.GetGroups(), model.Password, cts.Token);
				Snackbar.Add($"User '{model.LoginName}' created.", Severity.Success);
				Navigation.NavigateTo($"/ui/users/{Uri.EscapeDataString(model.LoginName)}");
			} catch (UserManagementException ex) {
				Snackbar.Add(ex.Message, Severity.Error);
			}
		}
	}

	public void Dispose() {
		_timer?.Dispose();
	}
}
