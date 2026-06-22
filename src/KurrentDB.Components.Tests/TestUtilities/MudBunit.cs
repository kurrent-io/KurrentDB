// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace KurrentDB.Components.Tests.TestUtilities;

// Shared bUnit setup for rendering MudBlazor components. It registers Mud services, sets loose JS interop
// (Mud performs interop on render), applies any test-specific service registrations, then renders a
// MudPopoverProvider into the context.
//
// Popover-eager components (MudDataGrid, MudTooltip, MudMenu, ...) create a popover during render and throw
// "Missing <MudPopoverProvider />" unless one is present in the render tree — in the running app MainLayout
// supplies it, but a bare component render does not.
//
// Test-specific services MUST be added via the 'configure' callback: the first render (of the provider)
// builds and locks the context's service provider, so nothing can be registered afterwards.
public static class MudBunit {
	public static BunitContext NewContext(Action<IServiceCollection>? configure = null) {
		var ctx = new BunitContext();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();
		configure?.Invoke(ctx.Services);
		ctx.Render<MudPopoverProvider>();
		return ctx;
	}
}
