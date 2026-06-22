// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MudBlazor;

namespace KurrentDB.Components.Users;

// Shared dialog options for the user-management dialogs (create/edit/reset password).
static class UserDialogDefaults {
	public static readonly DialogOptions Options = new() {
		MaxWidth = MaxWidth.Small,
		FullWidth = true,
		CloseOnEscapeKey = true
	};
}
