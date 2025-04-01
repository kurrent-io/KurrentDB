// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.UI.Services;

public class Preferences {
    public void ToggleTheme() => SetTheme(!DarkMode);

    public void SetTheme(bool darkMode) {
        DarkMode = darkMode;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool DarkMode { get; private set; } = true;

    public event EventHandler? ThemeChanged;
}
