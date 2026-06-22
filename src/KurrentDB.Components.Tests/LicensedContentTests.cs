// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using Bunit;
using EventStore.Plugins.Licensing;
using KurrentDB.Components.Licensed;
using KurrentDB.Components.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KurrentDB.Components.Tests;

// The license gate (LicensedContent, via WithLicense) renders its child content only when a commercial
// license is present. This is the mechanism the Cluster page uses to hide its historical time-series charts
// from unlicensed installs, so the show/hide behaviour is worth locking in.
public class LicensedContentTests {
	const string Gated = "licensed-feature-content";

	static IRenderedComponent<LicensedContent> RenderGate(BunitContext ctx, bool hasLicense) {
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddSingleton<ILicenseService>(new StubLicenseService(hasLicense));
		return ctx.Render<LicensedContent>(p => p.AddChildContent($"<span>{Gated}</span>"));
	}

	[Fact]
	public async Task Renders_child_content_when_a_license_is_present() {
		await using var ctx = new BunitContext();

		var cut = RenderGate(ctx, hasLicense: true);

		Assert.Contains(Gated, cut.Markup);
	}

	[Fact]
	public async Task Hides_child_content_when_no_license() {
		await using var ctx = new BunitContext();

		var cut = RenderGate(ctx, hasLicense: false);

		Assert.DoesNotContain(Gated, cut.Markup);
	}
}
