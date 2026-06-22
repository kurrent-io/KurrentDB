// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.UI.Theme;

// Icons sourced from the Navigator project. Most use 24x24 viewBox with stroke="currentColor"
// and fill="none". Stroke styling is baked into each path so the icons render consistently
// when passed to MudIcon (which uses a default 24x24 viewBox).
public static class KurrentIcons {
	const string Stroke = "fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";

	public const string Dashboard =
		$"<path {Stroke} d=\"M19.5 3h-15A1.5 1.5 0 0 0 3 4.5v15A1.5 1.5 0 0 0 4.5 21h15a1.5 1.5 0 0 0 1.5-1.5v-15A1.5 1.5 0 0 0 19.5 3ZM3 14h9M12 10h9M3 12.5v3M21 8.5v3M12 21V3M10.5 3h3M10.5 21h3\"/>";

	public const string Cluster =
		$"<path {Stroke} d=\"M22 2H2v8h20V2ZM22 14H2v8h20v-8Z\"/>" +
		"<path fill=\"currentColor\" d=\"M6.5 5h-1a.5.5 0 0 0-.5.5v1a.5.5 0 0 0 .5.5h1a.5.5 0 0 0 .5-.5v-1a.5.5 0 0 0-.5-.5ZM6.5 17h-1a.5.5 0 0 0-.5.5v1a.5.5 0 0 0 .5.5h1a.5.5 0 0 0 .5-.5v-1a.5.5 0 0 0-.5-.5ZM10.5 5h-1a.5.5 0 0 0-.5.5v1a.5.5 0 0 0 .5.5h1a.5.5 0 0 0 .5-.5v-1a.5.5 0 0 0-.5-.5ZM10.5 17h-1a.5.5 0 0 0-.5.5v1a.5.5 0 0 0 .5.5h1a.5.5 0 0 0 .5-.5v-1a.5.5 0 0 0-.5-.5Z\"/>";

	public const string Cog =
		$"<path {Stroke} d=\"M9.142 21.585a9.997 9.997 0 0 1-4.348-2.652 3 3 0 0 0-2.59-4.919A10.044 10.044 0 0 1 2.457 9H2.5a3 3 0 0 0 2.692-4.325A9.984 9.984 0 0 1 9.326 2.36a3 3 0 0 0 5.348 0 9.984 9.984 0 0 1 4.134 2.314A3 3 0 0 0 21.542 9a10.044 10.044 0 0 1 .255 5.015 3 3 0 0 0-2.59 4.919 9.998 9.998 0 0 1-4.349 2.651 3.001 3.001 0 0 0-5.716 0Z\"/>" +
		$"<path {Stroke} d=\"M12 15.5a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7Z\"/>";

	public const string Plugins =
		$"<path {Stroke} d=\"M2 12V6h4.5V5a3 3 0 0 1 6 0v1H17v6h2a3 3 0 1 1 0 6h-2v4H2v-4h2a3 3 0 1 0 0-6H2Z\"/>";

	public const string Users =
		$"<path {Stroke} d=\"M9.5 10a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7ZM16.304 3.5A3.498 3.498 0 0 1 18 6.5c0 1.273-.68 2.388-1.696 3M2 20.4v.6h15v-.6c0-2.24 0-3.36-.436-4.216a4 4 0 0 0-1.748-1.748C13.96 14 12.84 14 10.6 14H8.4c-2.24 0-3.36 0-4.216.436a4 4 0 0 0-1.748 1.748C2 17.04 2 18.16 2 20.4ZM22 21v-.6c0-2.24 0-3.36-.436-4.216a4 4 0 0 0-1.748-1.749\"/>";

	public const string Preferences =
		$"<path {Stroke} d=\"M20.75 5h-3M13.75 3v4M13.75 5h-11M6.75 12h-4M10.75 10v4M21.75 12h-11M20.75 19h-3M13.75 17v4M13.75 19h-11\"/>";

	public const string SignOut =
		$"<path {Stroke} d=\"M12 3H3v18h9M16.5 16.5 21 12l-4.5-4.5M8 12h13\"/>";

	public const string Projections =
		$"<path {Stroke} d=\"M4 7a2 2 0 1 0 0-4 2 2 0 0 0 0 4ZM4 13a1 1 0 1 0 0-2 1 1 0 0 0 0 2ZM4 20a1 1 0 1 0 0-2 1 1 0 0 0 0 2ZM10 12h12M10 19h12M10 5h12\"/>";

	public const string PersistentSubscriptions =
		$"<path {Stroke} d=\"M8 7a2 2 0 1 0 0-4 2 2 0 0 0 0 4ZM14 19H6.5c-2 0-3.5-1.458-3.5-3.5S4.5 12 6.5 12H10\"/>" +
		$"<path {Stroke} d=\"M10 12h7.5c2 0 3.5-1.458 3.5-3.5S19.5 5 17.5 5H10M3 5h3M18 19h3M16 21a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z\"/>";

	public const string Scavenges =
		$"<path {Stroke} d=\"M13.2 12.2 17.9 15.1\"/>" +
		$"<path {Stroke} d=\"M6.2 16.4s3.9-1.1 7-4.4c0 0 1.7-2.7 4.1-1.5s.9 4.5.9 4.5-.5 2.7-2.2 7.5c-.1 0-7 .4-9.8-6.1z\"/>" +
		$"<path {Stroke} d=\"M8.1 19.3s2.2-.8 4.4-2.8M11.8 21.5s1.2-1.1 2.5-3.7M16.6 10l4.1-7.9s.5-.9 1.3-.4.3 1.3.3 1.3l-4.2 8-1.5-1z\"/>" +
		$"<circle {Stroke} cx=\"6\" cy=\"12.3\" r=\"1\"/>" +
		$"<circle {Stroke} cx=\"2.7\" cy=\"15.9\" r=\"1.7\"/>" +
		$"<circle {Stroke} cx=\"4.6\" cy=\"20.1\" r=\".7\"/>";

	public const string Key =
		$"<path {Stroke} d=\"M11.434 12.15a4.932 4.932 0 0 1 1.315 4.793 4.963 4.963 0 0 1-3.533 3.514 4.994 4.994 0 0 1-4.82-1.307 4.931 4.931 0 0 1 .061-6.94 4.995 4.995 0 0 1 6.976-.06h.001ZM11.5 12 20 3.5M15.153 8.45l2.714 2.7L21.034 8l-2.715-2.7-3.166 3.15Z\"/>";

	public const string Administration =
		$"<path {Stroke} d=\"M2 14h2.5v8H2v-8ZM7 14h10v8H7v-8ZM19.5 14H22v8h-2.5v-8ZM2 22h20M3 11l9-9 9 9M9 11v3h6v-3\"/>";

	public const string Telemetry =
		$"<path {Stroke} d=\"M4 19h16M4 19v-9M8 19v-7M12 19v-11M16 19v-5M20 19v-9\"/>";

	public const string System =
		$"<path {Stroke} d=\"M3 5h18v12H3zM3 21h18M9 17v4M15 17v4\"/>";

	public const string Search =
		$"<path {Stroke} d=\"M21 21l-4.35-4.35M10.5 18a7.5 7.5 0 1 0 0-15 7.5 7.5 0 0 0 0 15Z\"/>";

	public const string Plus =
		$"<path {Stroke} d=\"m12.03 5-.018 14M5 12h14\"/>";

	public const string Delete =
		$"<path {Stroke} d=\"M4.5 5v17h15V5h-15ZM10 10v6.5M14 10v6.5M2 5h20M8 5l1.645-3h4.744L16 5H8Z\"/>";

	public const string Edit =
		$"<path {Stroke} d=\"M2.662 21.75h4.243L22.461 6.194 18.22 1.95 2.662 17.507v4.243ZM13.976 6.193l4.243 4.243\"/>";

	public const string Back =
		$"<path {Stroke} d=\"M2.9 12h16M8.9 18l-6-6 6-6\"/>" +
		$"<path {Stroke} d=\"M21 5 21 19\"/>";

	public const string Arrow =
		$"<path {Stroke} d=\"M21 12H3M15 6l6 6-6 6\"/>";

	public const string Play =
		$"<path {Stroke} d=\"M7.5 12V5.937l5.25 3.032 5.25 3.03-5.25 3.032-5.25 3.03V12Z\"/>";

	public const string Stop =
		$"<path {Stroke} d=\"M19.5 3h-15A1.5 1.5 0 0 0 3 4.5v15A1.5 1.5 0 0 0 4.5 21h15a1.5 1.5 0 0 0 1.5-1.5v-15A1.5 1.5 0 0 0 19.5 3Z\"/>";

	public const string Eye =
		$"<path {Stroke} d=\"M12 18c5.5 0 10-6 10-6s-4.5-6-10-6-10 6-10 6 4.5 6 10 6z\"/>" +
		$"<path {Stroke} d=\"M12 14.5c1.4 0 2.5-1.1 2.5-2.5S13.4 9.5 12 9.5 9.5 10.6 9.5 12s1.1 2.5 2.5 2.5z\"/>";

	// Fill-based icons (no stroke). MudIcon's default svg uses fill="currentColor".
	public const string Check =
		"<path d=\"M9.7 18.9c-.4 0-.7-.1-1-.4L3.2 13c-.5-.5-.5-1.4 0-2 .5-.5 1.4-.5 2 0l4.6 4.6L19.9 5.5c.5-.5 1.4-.5 2 0 .5.5.5 1.4 0 2l-11.2 11c-.3.3-.6.4-1 .4z\"/>";

	public const string Info =
		"<path fill-rule=\"evenodd\" clip-rule=\"evenodd\" d=\"M11.9 3c1.3 0 2.3 1 2.3 2.3s-1 2.3-2.3 2.3-2.3-1-2.3-2.3 1-2.3 2.3-2.3z\"/>" +
		"<path d=\"M14.8 18.3h-1V9.8c0-.8-.7-1.5-1.5-1.5h-1.4c-.8 0-1.5.7-1.5 1.5s.6 1.4 1.4 1.5v7h-1c-.8 0-1.5.7-1.5 1.5s.7 1.5 1.5 1.5h5c.8 0 1.5-.7 1.5-1.5s-.7-1.5-1.5-1.5z\"/>";

	public const string Error =
		"<path d=\"M16.5 4.4c-1.1-2.9-4.7-2.8-4.7-2.8s-3.5 0-4.6 2.8.8 4 .5 5.7 2.7-.1 2.1 2c-.3 1.3 2.1 1.3 2.1 1.3s2.5 0 2.1-1.3c-.6-2.1 2.5-.3 2.1-2-.3-1.6 1.7-2.8.4-5.7zM9.9 9.7c-.8 0-1.4-.4-1.5-.9-.1-.6.5-1.1 1.3-1.2s1.4.3 1.5.9c.1.5-.5 1-1.3 1.2zm5.5-.9c-.1.6-.7 1-1.5.9s-1.4-.6-1.3-1.1c.1-.6.7-1 1.5-.9.8-.1 1.3.5 1.3 1.1z\"/>" +
		"<path d=\"m20.4 20.1-5.6-2.7 5.6-2.7c.6-.3.9-1 .6-1.6-.3-.6-1-.9-1.6-.6L12 16l-7.4-3.6c-.6-.3-1.3 0-1.6.6-.3.6 0 1.3.6 1.6l5.6 2.7L3.6 20c-.6.3-.9 1-.6 1.6.2.4.6.7 1.1.7.2 0 .4 0 .5-.1l7.4-3.6 7.4 3.6c.2.1.4.1.5.1.5 0 .9-.3 1.1-.7.3-.5 0-1.2-.6-1.5z\"/>";

	public const string CheckCircle =
		$"<path {Stroke} d=\"M12 22c2.7 0 5.2-1.1 7.1-2.9C21 17.2 22 14.7 22 12c0-2.7-1.1-5.2-2.9-7.1C17.2 3.1 14.7 2 12 2 9.3 2 6.8 3.1 4.9 4.9 3.1 6.8 2 9.3 2 12c0 2.7 1.1 5.2 2.9 7.1C6.8 20.9 9.3 22 12 22z\"/>" +
		$"<path {Stroke} d=\"m8 12 3 3 6-6\"/>";

	public const string CrossCircle =
		$"<path {Stroke} d=\"M12 22c5.523 0 10-4.477 10-10S17.523 2 12 2 2 6.477 2 12s4.477 10 10 10ZM14.829 9.172l-5.657 5.657M9.172 9.172l5.656 5.657\"/>";

	public const string EnabledUser =
		$"<path {Stroke} d=\"M12 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8ZM21 22a9 9 0 1 0-18 0\"/>" +
		$"<path {Stroke} d=\"m15 18-4 4-2-2\"/>";

	public const string DisabledUser =
		$"<path {Stroke} d=\"M12 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8ZM21 22a9 9 0 1 0-18 0M10 18l4 4M14 18l-4 4\"/>";

	public const string Lock =
		$"<path {Stroke} d=\"M20 11H4c-.6 0-1 .4-1 1v9c0 .6.4 1 1 1h16c.6 0 1-.4 1-1v-9c0-.6-.4-1-1-1zM7 11V7c0-2.8 2.2-5 5-5s5 2.2 5 5v4M12 15v3\"/>";

	public const string PowerOff =
		$"<path {Stroke} d=\"M7.25 4a9.525 9.525 0 0 0-2.375 1.92A9.273 9.273 0 0 0 2.5 12.123c0 5.179 4.253 9.377 9.5 9.377s9.5-4.198 9.5-9.377c0-2.378-.897-4.55-2.375-6.203A9.508 9.508 0 0 0 16.75 4M12 2v10\"/>";

	// Stream uses a non-24x24 viewBox; scale to fit using a group transform.
	public const string Stream =
		"<g transform=\"matrix(0.064 0 0 0.064 1.8 0)\">" +
		"<path fill=\"currentColor\" d=\"M168.9 375.6c-2.1 0-4.1-.5-6-1.6L6 283.4c-3.7-2.1-6-6.1-6-10.4V92c0-4.3 2.3-8.2 6-10.4s8.3-2.1 12 0l156.9 90.6c3.7 2.1 6 6.1 6 10.4v181c0 4.3-2.3 8.2-6 10.4-1.8 1.1-3.9 1.6-6 1.6zM24 266.1l132.9 76.8V189.5L24 112.8v153.3z\"/>" +
		"<path fill=\"currentColor\" d=\"M238.2 335.6c-6.6 0-12-5.4-12-12V149.5L75.3 62.4c-5.7-3.3-7.7-10.7-4.4-16.4 3.3-5.7 10.7-7.7 16.4-4.4l156.9 90.6c3.7 2.1 6 6.1 6 10.4v181c0 6.6-5.3 12-12 12z\"/>" +
		"<path fill=\"currentColor\" d=\"M307.5 295.6c-6.6 0-12-5.4-12-12V109.5L144.6 22.4c-5.7-3.3-7.7-10.7-4.4-16.4 3.3-5.7 10.7-7.7 16.4-4.4l156.9 90.6c3.7 2.1 6 6.1 6 10.4v181c0 6.6-5.4 12-12 12z\"/>" +
		"</g>";
}
