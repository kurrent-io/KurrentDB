// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Services.Transport.Http;
using EventStore.Plugins.Authorization;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Http;

[TestFixture]
public class naive_uri_router_should : uri_router_should {
	public naive_uri_router_should() : base(() => new NaiveUriRouter()) {
	}
}

[TestFixture]
public class trie_uri_router_should : uri_router_should {
	public trie_uri_router_should()
		: base(() => new TrieUriRouter()) {
	}
}

public abstract class uri_router_should {
	private readonly Func<IUriRouter> _uriRouterFactory;

	private IUriRouter _router;

	protected uri_router_should(Func<IUriRouter> uriRouterFactory) {
		Ensure.NotNull(uriRouterFactory, "uriRouterFactory");
		_uriRouterFactory = uriRouterFactory;
	}

	[SetUp]
	public void SetUp() {
		_router = _uriRouterFactory();

		var p = new RequestParams(TimeSpan.Zero);
		_router.RegisterAction(
			new ControllerAction("/", HttpMethod.Get, Codec.NoCodecs, FakeController.SupportedCodecs, new Operation()), (x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/{placeholder}", HttpMethod.Get, Codec.NoCodecs, FakeController.SupportedCodecs, new Operation()),
			(x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/halt", HttpMethod.Get, Codec.NoCodecs, FakeController.SupportedCodecs, new Operation()),
			(x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/streams/{stream}/{event}/backward/{count}?embed={embed}", HttpMethod.Get,
				Codec.NoCodecs, FakeController.SupportedCodecs, new Operation()), (x, y) => p);
		_router.RegisterAction(
			new ControllerAction(
				"/projection/{name}?deleteStateStream={deleteStateStream}&deleteCheckpointStream={deleteCheckpointStream}",
				HttpMethod.Get, Codec.NoCodecs, FakeController.SupportedCodecs, new Operation()), (x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/s/stats/{*statPath}", HttpMethod.Get, Codec.NoCodecs,
				FakeController.SupportedCodecs, new Operation()), (x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/streams/$all/", HttpMethod.Get, Codec.NoCodecs, FakeController.SupportedCodecs, new Operation()),
			(x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/streams/$$all", HttpMethod.Get, Codec.NoCodecs, FakeController.SupportedCodecs, new Operation()),
			(x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/streams/$mono?param={param}", HttpMethod.Get, Codec.NoCodecs,
				FakeController.SupportedCodecs, new Operation()), (x, y) => p);

		_router.RegisterAction(
			new ControllerAction("/streams/test", HttpMethod.Get, Codec.NoCodecs, FakeController.SupportedCodecs, new Operation()),
			(x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/streams/test", HttpMethod.Post, Codec.NoCodecs, FakeController.SupportedCodecs, new Operation()),
			(x, y) => p);

		_router.RegisterAction(
			new ControllerAction("/t/{placeholder1}/{placholder2}/{placeholder3}", HttpMethod.Get, Codec.NoCodecs,
				FakeController.SupportedCodecs, new Operation()), (x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/t/{placeholder1}/{placholder2}/something", HttpMethod.Get, Codec.NoCodecs,
				FakeController.SupportedCodecs, new Operation()), (x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/t/{placeholder1}/something/{placeholder3}", HttpMethod.Get, Codec.NoCodecs,
				FakeController.SupportedCodecs, new Operation()), (x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/t/{placeholder1}/something/something", HttpMethod.Get, Codec.NoCodecs,
				FakeController.SupportedCodecs, new Operation()), (x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/t/something/{placholder2}/{placeholder3}", HttpMethod.Get, Codec.NoCodecs,
				FakeController.SupportedCodecs, new Operation()), (x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/t/something/{placholder2}/something", HttpMethod.Get, Codec.NoCodecs,
				FakeController.SupportedCodecs, new Operation()), (x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/t/something/something/{placeholder3}", HttpMethod.Get, Codec.NoCodecs,
				FakeController.SupportedCodecs, new Operation()), (x, y) => p);
		_router.RegisterAction(
			new ControllerAction("/t/something/something/something", HttpMethod.Get, Codec.NoCodecs,
				FakeController.SupportedCodecs, new Operation()), (x, y) => p);
	}

	[Test]
	public void detect_duplicate_route() {
		Assert.That(() =>
				_router.RegisterAction(
					new ControllerAction("/halt", HttpMethod.Get, Codec.NoCodecs, FakeController.SupportedCodecs, new Operation()),
					(x, y) => new RequestParams(TimeSpan.Zero)),
			Throws.Exception.InstanceOf<ArgumentException>().With.Message.EqualTo("Duplicate route."));
	}

	[Test]
	public void match_root() {
		var match = _router.GetAllUriMatches(Uri("/"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/", match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);
	}

	[Test]
	public void match_single_segment_path() {
		var match = _router.GetAllUriMatches(Uri("/halt"));
		Assert.AreEqual(2, match.Count);
		Assert.AreEqual("/{placeholder}", match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);
		Assert.AreEqual("/halt", match[1].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[1].ControllerAction.HttpMethod);
	}

	[Test, Ignore("ignore")]
	public void not_care_about_trailing_slash() {
		var match = _router.GetAllUriMatches(Uri("/streams/$all"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/streams/$all/", match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);

		match = _router.GetAllUriMatches(Uri("/streams/$all/"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/streams/$all/", match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);
	}

	[Test, Ignore("ignore")]
	public void not_care_about_trailing_slash2() {
		var match = _router.GetAllUriMatches(Uri("/streams/$$all"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/streams/$$all", match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);

		match = _router.GetAllUriMatches(Uri("/streams/$$all/"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/streams/$$all", match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);
	}

	[Test]
	public void care_about_trailing_slash() {
		var match = _router.GetAllUriMatches(Uri("/streams/$all/"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/streams/$all/", match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);

		match = _router.GetAllUriMatches(Uri("/streams/$all"));
		Assert.AreEqual(0, match.Count);
	}

	[Test]
	public void care_about_trailing_slash2() {
		var match = _router.GetAllUriMatches(Uri("/streams/$$all"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/streams/$$all", match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);

		match = _router.GetAllUriMatches(Uri("/streams/$$all/"));
		Assert.AreEqual(0, match.Count);
	}

	[Test]
	public void match_route_with_dollar_sign() {
		var match = _router.GetAllUriMatches(Uri("/streams/$mono"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/streams/$mono?param={param}", match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);

		match = _router.GetAllUriMatches(Uri("/streams/$mono?param=bla"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/streams/$mono?param={param}", match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);
	}

	[Test]
	public void match_complex_route_with_placeholders_and_query_params() {
		var match = _router.GetAllUriMatches(Uri("/streams/test-stream/10/backward/20?embed=true"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/streams/{stream}/{event}/backward/{count}?embed={embed}",
			match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);
	}

	[Test]
	public void match_complex_route_with_placeholders_and_query_params_when_no_query_params_are_set() {
		var match = _router.GetAllUriMatches(Uri("/streams/test-stream/head/backward/20"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/streams/{stream}/{event}/backward/{count}?embed={embed}",
			match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);
	}

	[Test]
	public void not_match_partial_route_match() {
		var match = _router.GetAllUriMatches(Uri("/projection/proj/something"));
		Assert.AreEqual(0, match.Count);
	}

	[Test]
	public void match_all_possible_alternatives() {
		var match = _router.GetAllUriMatches(Uri("/t/something/something/something"));
		Assert.AreEqual(8, match.Count);
		Assert.AreEqual(new[] {
				"/t/{placeholder1}/{placholder2}/{placeholder3}",
				"/t/{placeholder1}/{placholder2}/something",
				"/t/{placeholder1}/something/{placeholder3}",
				"/t/{placeholder1}/something/something",
				"/t/something/{placholder2}/{placeholder3}",
				"/t/something/{placholder2}/something",
				"/t/something/something/{placeholder3}",
				"/t/something/something/something"
			},
			match.Select(x => x.ControllerAction.UriTemplate).ToArray());
	}

	[Test]
	public void match_same_routes_with_different_http_methods() {
		var match = _router.GetAllUriMatches(Uri("/streams/test"));
		Assert.AreEqual(2, match.Count);
		Assert.AreEqual("/streams/test", match[0].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Get, match[0].ControllerAction.HttpMethod);
		Assert.AreEqual("/streams/test", match[1].ControllerAction.UriTemplate);
		Assert.AreEqual(HttpMethod.Post, match[1].ControllerAction.HttpMethod);
	}

	[Test]
	public void match_greedy_route_with_bare_minimum_of_uri() {
		var match = _router.GetAllUriMatches(Uri("/s/stats/test"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/s/stats/{*statPath}", match[0].ControllerAction.UriTemplate);
	}

	[Test]
	public void match_greedy_route_and_catch_long_uri() {
		var match = _router.GetAllUriMatches(Uri("/s/stats/some/long/stat/path"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/s/stats/{*statPath}", match[0].ControllerAction.UriTemplate);
	}

	[Test]
	public void match_greedy_route_with_empty_path_part_starting_with_slash() {
		var match = _router.GetAllUriMatches(Uri("/s/stats/"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/s/stats/{*statPath}", match[0].ControllerAction.UriTemplate);
	}

	[Test]
	public void not_match_greedy_route_with_empty_path_part_without_slash() {
		var match = _router.GetAllUriMatches(Uri("/s/stats"));
		Assert.AreEqual(0, match.Count);
	}

	[Test]
	public void match_greedy_route_in_the_root_to_any_path() {
		var tmpRouter = _uriRouterFactory();
		tmpRouter.RegisterAction(
			new ControllerAction("/{*greedy}", HttpMethod.Get, Codec.NoCodecs, FakeController.SupportedCodecs, new Operation()),
			(x, y) => new RequestParams(TimeSpan.Zero));

		var match = tmpRouter.GetAllUriMatches(Uri("/"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/{*greedy}", match[0].ControllerAction.UriTemplate);

		match = tmpRouter.GetAllUriMatches(Uri("/something"));
		Assert.AreEqual(1, match.Count);
		Assert.AreEqual("/{*greedy}", match[0].ControllerAction.UriTemplate);
	}

	private Uri Uri(string relativePath) {
		return new Uri("http://localhost:12345" + relativePath);
	}
}
