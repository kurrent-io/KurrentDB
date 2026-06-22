// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Messages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace KurrentDB.Components.Users;

public partial class UserDetail : ComponentBase {
	[Inject] UserManagementService UserService { get; set; } = null!;
	[Inject] IDialogService DialogService { get; set; } = null!;
	[Inject] ISnackbar Snackbar { get; set; } = null!;
	[Inject] NavigationManager Navigation { get; set; } = null!;

	[CascadingParameter] Task<AuthenticationState> AuthenticationState { get; set; }
	[Parameter] public string LoginName { get; set; }

	UserManagementMessage.UserData _user;
	bool _loading = true;
	string _loadError;
	ClaimsPrincipal _principal;

	protected override async Task OnInitializedAsync() {
		var authState = await AuthenticationState;
		_principal = authState.User;
		await LoadUser();
	}

	async Task LoadUser() {
		_loadError = null;
		try {
			using var cts = new CancellationTokenSource(5000);
			_user = await UserService.GetAsync(_principal, LoginName, cts.Token);
		} catch (UserManagementException ex) when (ex.Error == UserManagementMessage.Error.NotFound) {
			_user = null;   // genuine not-found; the markup renders the not-found message
		} catch (Exception ex) {
			// Distinguish a real failure (timeout, access denied, etc.) from "not found" so a
			// transient error isn't mislabelled as a missing user.
			_user = null;
			_loadError = ex is UserManagementException ? ex.Message : "Failed to load user. Please try again.";
		} finally {
			_loading = false;
		}
	}

	async Task EditUser() {
		var parameters = new DialogParameters<EditUserDialog> {
			{ x => x.LoginName, _user.LoginName },
			{ x => x.FullName, _user.FullName },
			{ x => x.Group, _user.Groups?.FirstOrDefault() ?? "" }
		};
		var dialog = await DialogService.ShowAsync<EditUserDialog>("Edit User", parameters, UserDialogDefaults.Options);
		var result = await dialog.Result;
		if (result is { Canceled: false, Data: EditUserDialog.EditUserModel model })
			await RunGuarded(
				ct => UserService.UpdateAsync(_principal, _user.LoginName, model.FullName, model.GetGroups(), ct),
				$"User '{_user.LoginName}' updated.",
				LoadUser);
	}

	Task DeleteUser() => ConfirmAndRun(
		"Delete User", $"Are you sure you want to delete user '{_user.LoginName}'?", "Delete",
		ct => UserService.DeleteAsync(_principal, _user.LoginName, ct),
		$"User '{_user.LoginName}' deleted.",
		() => { Navigation.NavigateTo("/ui/users"); return Task.CompletedTask; });

	Task EnableUser() => ConfirmAndRun(
		"Enable User", $"Are you sure you want to enable user '{_user.LoginName}'?", "Enable",
		ct => UserService.EnableAsync(_principal, _user.LoginName, ct),
		$"User '{_user.LoginName}' enabled.",
		LoadUser);

	Task DisableUser() => ConfirmAndRun(
		"Disable User", $"Are you sure you want to disable user '{_user.LoginName}'?", "Disable",
		ct => UserService.DisableAsync(_principal, _user.LoginName, ct),
		$"User '{_user.LoginName}' disabled.",
		LoadUser);

	async Task ResetPassword() {
		var parameters = new DialogParameters<ResetPasswordDialog> {
			{ x => x.LoginName, _user.LoginName }
		};
		var dialog = await DialogService.ShowAsync<ResetPasswordDialog>("Reset Password", parameters, UserDialogDefaults.Options);
		var result = await dialog.Result;
		if (result is { Canceled: false, Data: ResetPasswordDialog.ResetPasswordModel model })
			await RunGuarded(
				ct => UserService.ResetPasswordAsync(_principal, _user.LoginName, model.NewPassword, ct),
				$"Password for '{_user.LoginName}' has been reset.",
				() => Task.CompletedTask);   // no reload needed; password isn't displayed
	}

	// Shows a confirmation dialog, then runs the action with the standard success/error handling.
	async Task ConfirmAndRun(string title, string message, string yesText,
		Func<CancellationToken, ValueTask> action, string successMessage, Func<Task> onSuccess) {
		var confirmed = await DialogService.ShowMessageBox(title, message, yesText: yesText, cancelText: "Cancel");
		if (confirmed == true)
			await RunGuarded(action, successMessage, onSuccess);
	}

	// Runs a user-management action with a timeout, surfacing success/failure via the snackbar.
	async Task RunGuarded(Func<CancellationToken, ValueTask> action, string successMessage, Func<Task> onSuccess) {
		try {
			using var cts = new CancellationTokenSource(5000);
			await action(cts.Token);
			Snackbar.Add(successMessage, Severity.Success);
			await onSuccess();
		} catch (UserManagementException ex) {
			Snackbar.Add(ex.Message, Severity.Error);
		}
	}
}
