using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace KurrentDB.Services;

sealed class IdentityRedirectManager(NavigationManager navigationManager) {
	public const string StatusCookieName = "Identity.StatusMessage";

	private static readonly CookieBuilder StatusCookieBuilder = new() {
		SameSite = SameSiteMode.Strict,
		HttpOnly = true,
		IsEssential = true,
		MaxAge = TimeSpan.FromSeconds(5),
	};

	[DoesNotReturn]
	public void RedirectTo(string uri) {
		uri ??= "";

		if (!Uri.IsWellFormedUriString(uri, UriKind.Relative)) {
			uri = navigationManager.ToBaseRelativePath(uri);
		}

		navigationManager.NavigateTo(uri);
		throw new InvalidOperationException($"{nameof(IdentityRedirectManager)} can only be used during static rendering.");
	}

	[DoesNotReturn]
	public void RedirectTo(string uri, Dictionary<string, object> queryParameters) {
		var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
		var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
		RedirectTo(newUri);
	}

	[DoesNotReturn]
	public void RedirectToWithStatus(string uri, string message, HttpContext context) {
		context.Response.Cookies.Append(StatusCookieName, message, StatusCookieBuilder.Build(context));
		RedirectTo(uri);
	}

	private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

	[DoesNotReturn]
	public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

	[DoesNotReturn]
	public void RedirectToCurrentPageWithStatus(string message, HttpContext context)
		=> RedirectToWithStatus(CurrentPath, message, context);
}
