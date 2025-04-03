// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using EventStore.Common.Utils;
using EventStore.Transport.Http.EntityManagement;

namespace EventStore.Core.Services.Transport.Http;

public interface IUriRouter {
	void RegisterAction(ControllerAction action, Func<HttpEntityManager, UriTemplateMatch, RequestParams> handler);
	List<UriToActionMatch> GetAllUriMatches(Uri uri);
	IEnumerable<ControllerAction> Actions { get; }
}

public class TrieUriRouter : IUriRouter {
	private const string Placeholder = "{}";
	private const string GreedyPlaceholder = "{*}";

	private readonly RouterNode _root = new();
	private readonly List<ControllerAction> _registeredRoutes = [];

	public void RegisterAction(ControllerAction action, Func<HttpEntityManager, UriTemplateMatch, RequestParams> handler) {
		Ensure.NotNull(action);
		Ensure.NotNull(handler);

		var segments = new Uri($"http://fake{action.UriTemplate}", UriKind.Absolute).Segments;
		RouterNode node = _root;
		foreach (var sgm in segments) {
			var segment = Uri.UnescapeDataString(sgm);
			string path = segment.StartsWith("{*")
				? GreedyPlaceholder
				: segment.StartsWith('{') ? Placeholder : segment;

			if (!node.Children.TryGetValue(path, out var child)) {
				child = new();
				node.Children.Add(path, child);
			}

			node = child;
		}

		if (node.LeafRoutes.Contains(x => x.Action.Equals(action)))
			throw new ArgumentException("Duplicate route.");
		node.LeafRoutes.Add(new HttpRoute(action, handler));

		_registeredRoutes.Add(action);
	}

	public List<UriToActionMatch> GetAllUriMatches(Uri uri) {
		var matches = new List<UriToActionMatch>();
		var baseAddress = new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri;

		var segments = new string[uri.Segments.Length];
		for (int i = 0; i < uri.Segments.Length; i++) {
			segments[i] = Uri.UnescapeDataString(uri.Segments[i]);
		}

		GetAllUriMatches(_root, baseAddress, uri, segments, 0, matches);

		return matches;
	}

	public IEnumerable<ControllerAction> Actions => _registeredRoutes;

	private static void GetAllUriMatches(RouterNode node, Uri baseAddress, Uri uri, string[] segments, int index, List<UriToActionMatch> matches) {
		RouterNode child;

		if (index == segments.Length) {
			// /stats/ should match /stats/{*greedyStatsPath}
			if (uri.OriginalString.EndsWith('/') && node.Children.TryGetValue(GreedyPlaceholder, out child))
				AddMatchingRoutes(child.LeafRoutes, baseAddress, uri, matches);

			AddMatchingRoutes(node.LeafRoutes, baseAddress, uri, matches);
			return;
		}

		if (node.Children.TryGetValue(GreedyPlaceholder, out child))
			GetAllUriMatches(child, baseAddress, uri, segments, segments.Length, matches);
		if (node.Children.TryGetValue(Placeholder, out child))
			GetAllUriMatches(child, baseAddress, uri, segments, index + 1, matches);
		if (node.Children.TryGetValue(segments[index], out child))
			GetAllUriMatches(child, baseAddress, uri, segments, index + 1, matches);
	}

	private static void AddMatchingRoutes(IList<HttpRoute> routes, Uri baseAddress, Uri uri,
		List<UriToActionMatch> matches) {
		for (int i = 0; i < routes.Count; ++i) {
			var route = routes[i];
			var match = route.UriTemplate.Match(baseAddress, uri);
			if (match != null)
				matches.Add(new(match, route.Action, route.Handler));
		}
	}

	private class RouterNode {
		public readonly Dictionary<string, RouterNode> Children = new();
		public readonly List<HttpRoute> LeafRoutes = [];
	}
}

public class NaiveUriRouter : IUriRouter {
	private readonly List<HttpRoute> _actions = [];

	public IEnumerable<ControllerAction> Actions => _actions.Select(x => x.Action);

	public void RegisterAction(ControllerAction action, Func<HttpEntityManager, UriTemplateMatch, RequestParams> handler) {
		if (_actions.Contains(x => x.Action.Equals(action)))
			throw new ArgumentException("Duplicate route.");
		_actions.Add(new HttpRoute(action, handler));
	}

	public List<UriToActionMatch> GetAllUriMatches(Uri uri) {
		var matches = new List<UriToActionMatch>();
		var baseAddress = new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri;
		for (int i = 0; i < _actions.Count; ++i) {
			var route = _actions[i];
			var match = route.UriTemplate.Match(baseAddress, uri);
			if (match != null)
				matches.Add(new UriToActionMatch(match, route.Action, route.Handler));
		}

		return matches;
	}
}

internal class HttpRoute(ControllerAction action, Func<HttpEntityManager, UriTemplateMatch, RequestParams> handler) {
	public readonly ControllerAction Action = action;
	public readonly Func<HttpEntityManager, UriTemplateMatch, RequestParams> Handler = handler;
	public readonly UriTemplate UriTemplate = new(action.UriTemplate);
}
