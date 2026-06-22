// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using System.Threading.Tasks;
using Bunit;
using KurrentDB.Components;
using KurrentDB.Components.Tests.TestUtilities;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KurrentDB.Components.Tests;

// NotAuthorizedView is what Routes.razor's AuthorizeRouteView renders when a page's [Authorize(Policy=...)]
// denies. It must branch on identity: an authenticated-but-unauthorized user sees an access-denied notice
// (logging in again wouldn't help); an anonymous user is redirected to the login form.
public class NotAuthorizedViewTests {
	static ClaimsPrincipal Authenticated =>
		new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "bob")], authenticationType: "test"));

	// No authentication type => Identity.IsAuthenticated is false.
	static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());

	[Fact]
	public async Task Authenticated_user_sees_access_denied_and_is_not_redirected() {
		await using var ctx = MudBunit.NewContext();

		var cut = ctx.Render<NotAuthorizedView>(p => p.Add(x => x.User, Authenticated));

		Assert.Contains("You do not have access to this resource", cut.Markup);
		var nav = ctx.Services.GetRequiredService<NavigationManager>();
		Assert.DoesNotContain("ui/login", nav.Uri);   // stayed put — no redirect
	}

	[Fact]
	public async Task Anonymous_user_is_redirected_to_login() {
		await using var ctx = MudBunit.NewContext();

		ctx.Render<NotAuthorizedView>(p => p.Add(x => x.User, Anonymous));

		var nav = ctx.Services.GetRequiredService<NavigationManager>();
		Assert.Contains("ui/login", nav.Uri);
	}
}
