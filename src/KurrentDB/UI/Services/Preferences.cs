// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace KurrentDB.UI.Services;

public class Preferences {
	// A dedicated, non-HttpOnly cookie (written client-side on toggle — see MainLayout) so it's readable
	// server-side on the next render. That lets us seed the theme before first paint, avoiding the flicker
	// browser localStorage would cause in Blazor Server (it isn't readable until the circuit connects).
	public const string CookieName = "kurrentdb.theme";
	const string StateKey = "kurrentdb.theme";

	public Preferences(PersistentComponentState state, IHttpContextAccessor httpContextAccessor) {
		if (httpContextAccessor.HttpContext is { } ctx) {
			// Server render (prerender / static): the cookie is the source of truth, except a ?theme= param
			// (set on links from other nodes, whose cookie we can't see) wins so cross-node navigation keeps
			// the theme. The value is handed to the interactive circuit via persistent component state so its
			// first render matches.
			string? pref = ctx.Request.Query["theme"];
			if (pref is not "light" and not "dark")
				pref = ctx.Request.Cookies[CookieName];
			DarkMode = !string.Equals(pref, "light", StringComparison.Ordinal);
			state.RegisterOnPersisting(() => {
				state.PersistAsJson(StateKey, DarkMode);
				return Task.CompletedTask;
			});
		} else if (state.TryTakeFromJson<bool>(StateKey, out var darkMode)) {
			// Interactive circuit (no HttpContext): adopt the value the prerender captured.
			DarkMode = darkMode;
		}
		// else: nothing to restore — fall back to the dark default below.
	}

	public bool DarkMode { get; private set; } = true;

	public void ToggleTheme() => SetTheme(!DarkMode);

	public void SetTheme(bool darkMode) {
		DarkMode = darkMode;
		ThemeChanged?.Invoke(this, EventArgs.Empty);
	}

	public event EventHandler? ThemeChanged;
}
