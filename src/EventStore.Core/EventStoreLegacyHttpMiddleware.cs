// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Services.Transport.Http;
using EventStore.Plugins.Authorization;
using EventStore.Transport.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EventStore.Core;

public static class EventStoreLegacyHttpMiddleware {
	public static IEndpointRouteBuilder MapLegacyHttp(
		this IEndpointRouteBuilder app, RequestDelegate dispatcher,
		params IHttpService[] httpServices) {
		if (app == null)
			throw new ArgumentNullException(nameof(app));
		if (httpServices == null)
			throw new ArgumentNullException(nameof(httpServices));

		var actions = httpServices
			.SelectMany(x => x.Actions.Select(action =>
				(ConvertToRoute(action.UriTemplate), action.HttpMethod, action.Operation)))
			.ToArray();

		var actionsToRegister = actions
			.Concat(actions
				.Select(_ => _.Item1)
				.Select(route => (route, HttpMethod.Options, new Func<UriTemplateMatch, Operation>(_ => new Operation(Operations.Node.Options)))))
			.Distinct(RouteAndMethodComparer.Instance);

		foreach (var action in actionsToRegister) {
			RegisterRoute(app, action);
		}

		return app;

		void RegisterRoute(
			IEndpointRouteBuilder builder,
			(string route, string method, Func<UriTemplateMatch, Operation> operation) action) => builder
				.MapMethods(
					action.route,
					action.method == HttpMethod.Get
						? new[] { HttpMethod.Get, HttpMethod.Head }
						: new[] { action.method },
					dispatcher)
				.WithMetadata(action.operation);

		static string ConvertToRoute(string uriTemplate) {
			var route = uriTemplate.Split('?').First();
			return System.Net.WebUtility.UrlDecode(route.EndsWith("/") ? route[..^1] : route);
		}
	}
	class RouteAndMethodComparer : EqualityComparer<(string, string, Func<UriTemplateMatch, Operation>)> {
		public static readonly RouteAndMethodComparer Instance = new RouteAndMethodComparer();
		public override bool Equals((string, string, Func<UriTemplateMatch, Operation>) x, (string, string, Func<UriTemplateMatch, Operation>) y) {
			return string.Equals(x.Item1, y.Item1, StringComparison.Ordinal) &&
				   string.Equals(x.Item2, y.Item2, StringComparison.Ordinal);
		}

		public override int GetHashCode((string, string, Func<UriTemplateMatch, Operation>) obj) {
			return HashCode.Combine(obj.Item1, obj.Item2);
		}
	}
}
