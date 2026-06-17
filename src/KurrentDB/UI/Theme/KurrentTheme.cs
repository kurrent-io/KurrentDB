// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MudBlazor;

namespace KurrentDB.UI.Theme;

public static class KurrentTheme {
	// Brand colors
	public const string Primary = "#b75781";
	public const string Secondary = "#631b3a";
	public const string Surface = "#171717";
	public const string Background = "#101010";
	public const string BackgroundGray = "#151515";
	public const string AppbarBackground = "#631b3a";
	public const string DrawerBackground = "#101010";

	// Text
	public const string TextPrimary = "#d4d4d4";
	public const string TextSecondary = "#999999";
	public const string TextDisabled = "#ffffff33";

	// Semantic
	public const string ActionDefault = "#74718e";

	// Chart palette
	public static readonly string[] ChartPalette = [
		"#b75781", // Primary rose
		"#999999", // Secondary gray
		"#d4a0c0", // Light rose
		"#666666", // Dark gray
		"#8a3d5c", // Deep rose
		"#cccccc", // Light gray
	];

	// Donut segments render as literal SVG fills (not CSS vars), so the "remaining" track has to flip per
	// theme: a gray that reads on the dark background vs. one that reads on the light one. Used portion is
	// brand rose in both. Pass the current theme via DonutPalette(darkMode).
	public const string DonutUsed = "#b75781";
	public const string DonutRemainingDark = "#333333";
	public const string DonutRemainingLight = "#e0e0e3";

	public static string[] DonutPalette(bool darkMode) => [
		DonutUsed,
		darkMode ? DonutRemainingDark : DonutRemainingLight,
	];

	public static readonly PaletteDark DarkPalette = new() {
		Primary = KurrentTheme.Primary,
		Secondary = KurrentTheme.Secondary,
		Surface = KurrentTheme.Surface,
		Background = KurrentTheme.Background,
		BackgroundGray = KurrentTheme.BackgroundGray,
		AppbarText = TextPrimary,
		AppbarBackground = KurrentTheme.AppbarBackground,
		DrawerBackground = KurrentTheme.DrawerBackground,
		ActionDefault = KurrentTheme.ActionDefault,
		ActionDisabled = "#b0b0b099",
		ActionDisabledBackground = "#605f6d4d",
		TextPrimary = KurrentTheme.TextPrimary,
		TextSecondary = KurrentTheme.TextSecondary,
		TextDisabled = KurrentTheme.TextDisabled,
		DrawerIcon = TextPrimary,
		DrawerText = TextPrimary,
		GrayLight = "#2a2833",
		GrayLighter = "#1e1e2d",
		Info = "#4a86ff",
		Success = KurrentTheme.Primary, // use brand rose for success instead of green
		Warning = "#ffb545",
		Error = "#ed405d",
		ErrorLighten = "#f0637b",
		ErrorDarken = "#ea1d40",
		ErrorContrastText = "#ffffff",
		LinesDefault = "#33323e",
		TableLines = "#33323e",
		Divider = "#292838",
		OverlayLight = "#1e1e2d80",
	};

	// Branded light theme, mirroring DarkPalette field-for-field. Shares the brand accents (rose Primary,
	// plum Secondary/app bar) so identity is consistent across themes; only the surfaces, text and lines
	// flip to light values. The app bar stays plum (not white) so the white logo remains visible.
	public static readonly PaletteLight LightPalette = new() {
		Primary = KurrentTheme.Primary,
		Secondary = KurrentTheme.Secondary,
		Surface = "#ffffff",
		Background = "#f7f7f8",
		BackgroundGray = "#f0f0f2",
		AppbarText = "#ffffff",                            // white text/logo on the brand-plum app bar
		AppbarBackground = KurrentTheme.AppbarBackground,  // plum #631b3a, same bar as dark mode
		DrawerBackground = "#ffffff",
		ActionDefault = KurrentTheme.ActionDefault,
		ActionDisabled = "#00000042",
		ActionDisabledBackground = "#0000001f",
		TextPrimary = "#1f1f23",
		TextSecondary = "#6f6e77",
		TextDisabled = "#00000061",
		DrawerIcon = "#1f1f23",
		DrawerText = "#1f1f23",
		GrayLight = "#e8e8ea",
		GrayLighter = "#f4f4f6",
		Info = "#3b78e7",
		Success = KurrentTheme.Primary, // brand rose for success instead of green, matching dark
		Warning = "#d98008",            // darker amber than dark mode for contrast on a light background
		Error = "#d32f4a",
		ErrorLighten = "#ed405d",
		ErrorDarken = "#b3243c",
		ErrorContrastText = "#ffffff",
		LinesDefault = "#e0e0e3",
		TableLines = "#e0e0e3",
		Divider = "#e6e6e9",
		OverlayLight = "#ffffffcc",
	};
}
