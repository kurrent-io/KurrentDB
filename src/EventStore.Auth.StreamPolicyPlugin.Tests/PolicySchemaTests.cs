// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using Xunit;

namespace EventStore.Auth.StreamPolicyPlugin.Tests;

public class PolicySchemaTests {
	private static readonly JsonSerializerOptions SerializeOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	[Theory]
	[MemberData(nameof(InvalidPolicies))]
	public void when_deserializing_invalid_policies(string data) {
		Assert.Throws<ArgumentNullException>(() =>
			JsonSerializer.Deserialize<Schema.Policy>(data, SerializeOptions));
	}

	[Theory]
	[MemberData(nameof(InvalidStreamRules))]
	public void when_deserializing_invalid_stream_rules(string data) {
		Assert.Throws<ArgumentNullException>(() =>
			JsonSerializer.Deserialize<Schema.StreamRule>(data, SerializeOptions));
	}

	[Theory]
	[MemberData(nameof(InvalidDefaultStreamRules))]
	public void when_deserializing_invalid_default_stream_rules(string data) {
		Assert.Throws<ArgumentNullException>(() =>
			JsonSerializer.Deserialize<Schema.DefaultStreamRules>(data, SerializeOptions));
	}

	[Theory]
	[MemberData(nameof(InvalidAccessPolicies))]
	public void when_deserializing_invalid_access_policies(string data) {
		Assert.Throws<ArgumentNullException>(() =>
			JsonSerializer.Deserialize<Schema.AccessPolicy>(data, SerializeOptions));
	}

	public static IEnumerable<object[]> InvalidAccessPolicies() {
		yield return ["""{"$r":null, "$w":[], "$d": [], "$mr": [], "$mw": []}"""];
		yield return ["""{"$r": [], "$w":null, "$d": [], "$mr": [], "$mw": []}"""];
		yield return ["""{"$r": [], "$w":[], "$d": null, "$mr": [], "$mw": []}"""];
		yield return ["""{"$r": [], "$w":[], "$d": [], "$mr": null, "$mw": []}"""];
		yield return ["""{"$r": [], "$w":[], "$d": [], "$mr": [], "$mw": null}"""];
	}

	public static IEnumerable<object[]> InvalidDefaultStreamRules() {
		yield return ["""{"userStreams":null, "systemStreams":"test"}"""];
		yield return ["""{"userStreams":"", "systemStreams":"test"}"""];
		yield return ["""{"userStreams":"test", "systemStreams":null}"""];
		yield return ["""{"userStreams":"test", "systemStreams":""}"""];
	}

	public static IEnumerable<object[]> InvalidStreamRules() {
		yield return ["""{"startsWith":null, "policy":"test"}"""];
		yield return ["""{"startsWith":"", "policy":"test"}"""];
		yield return ["""{"startsWith":"test", "policy":null}"""];
		yield return ["""{"startsWith":"test", "policy":""}"""];
	}

	public static IEnumerable<object[]> InvalidPolicies() {
		yield return [
			"""
			{
			  "streamPolicies": null,
			  "streamRules": [],
			  "defaultStreamRules": {
			    "userStreams": null,
			    "systemStreams": "adminsDefault"
			  }
			}
			"""
		];
		yield return [
			"""
			{
			  "streamPolicies": {
			    "publicDefault": {
			      "$r": ["$all"],
			      "$w": ["$all"],
			      "$d": ["$all"],
			      "$mr": ["$all"],
			      "$mw": ["$all"]
			    }
			  },
			  "streamRules": null,
			  "defaultStreamRules": {
			    "userStreams": null,
			    "systemStreams": "adminsDefault"
			  }
			}
			"""
		];
		yield return [
			"""
			{
			  "streamPolicies": {
			    "publicDefault": {
			      "$r": ["$all"],
			      "$w": ["$all"],
			      "$d": ["$all"],
			      "$mr": ["$all"],
			      "$mw": ["$all"]
			    }
			  },
			  "streamRules": [],
			  "defaultStreamRules": null
			}
			"""
		];
	}
}
