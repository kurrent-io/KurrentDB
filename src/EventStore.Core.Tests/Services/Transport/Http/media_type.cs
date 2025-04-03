// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Common.Utils;
using EventStore.Transport.Http;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Http;

[TestFixture]
public class media_type {
	[Test]
	public void parses_generic_wildcard() {
		var c = MediaType.Parse("*/*");

		Assert.AreEqual("*", c.Type);
		Assert.AreEqual("*", c.Subtype);
		Assert.AreEqual("*/*", c.Range);
		Assert.AreEqual(1.0f, c.Priority);
		Assert.IsFalse(c.EncodingSpecified);
		Assert.IsNull(c.Encoding);
	}

	[Test]
	public void parses_generic_wildcard_with_priority() {
		var c = MediaType.Parse("*/*;q=0.4");

		Assert.AreEqual("*", c.Type);
		Assert.AreEqual("*", c.Subtype);
		Assert.AreEqual("*/*", c.Range);
		Assert.AreEqual(0.4f, c.Priority);
		Assert.IsFalse(c.EncodingSpecified);
		Assert.IsNull(c.Encoding);
	}

	[Test]
	public void parses_generic_wildcard_with_priority_and_other_param() {
		var c = MediaType.Parse("*/*;q=0.4;z=7");

		Assert.AreEqual("*", c.Type);
		Assert.AreEqual("*", c.Subtype);
		Assert.AreEqual("*/*", c.Range);
		Assert.AreEqual(0.4f, c.Priority);
		Assert.IsFalse(c.EncodingSpecified);
		Assert.IsNull(c.Encoding);
	}

	[Test]
	public void parses_partial_wildcard() {
		var c = MediaType.Parse("text/*");

		Assert.AreEqual("text", c.Type);
		Assert.AreEqual("*", c.Subtype);
		Assert.AreEqual("text/*", c.Range);
		Assert.AreEqual(1f, c.Priority);
		Assert.IsFalse(c.EncodingSpecified);
		Assert.IsNull(c.Encoding);
	}

	[Test]
	public void parses_specific_media_range_with_priority() {
		var c = MediaType.Parse("application/xml;q=0.7");

		Assert.AreEqual("application", c.Type);
		Assert.AreEqual("xml", c.Subtype);
		Assert.AreEqual("application/xml", c.Range);
		Assert.AreEqual(0.7f, c.Priority);
		Assert.IsFalse(c.EncodingSpecified);
		Assert.IsNull(c.Encoding);
	}

	[Test]
	public void parses_with_encoding() {
		var c = MediaType.Parse($"{ContentType.Json};charset=utf-8");

		Assert.AreEqual("application", c.Type);
		Assert.AreEqual("json", c.Subtype);
		Assert.AreEqual(ContentType.Json, c.Range);
		Assert.IsTrue(c.EncodingSpecified);
		Assert.AreEqual(Helper.UTF8NoBom, c.Encoding);
	}

	[Test]
	public void parses_with_encoding_and_priority() {
		var c = MediaType.Parse($"{ContentType.Json};q=0.3;charset=utf-8");

		Assert.AreEqual("application", c.Type);
		Assert.AreEqual("json", c.Subtype);
		Assert.AreEqual(ContentType.Json, c.Range);
		Assert.AreEqual(0.3f, c.Priority);
		Assert.IsTrue(c.EncodingSpecified);
		Assert.AreEqual(Helper.UTF8NoBom, c.Encoding);
	}

	[Test]
	public void parses_unknown_encoding() {
		var c = MediaType.Parse($"{ContentType.Json};charset=woftam");

		Assert.AreEqual("application", c.Type);
		Assert.AreEqual("json", c.Subtype);
		Assert.AreEqual(ContentType.Json, c.Range);
		Assert.IsTrue(c.EncodingSpecified);
		Assert.IsNull(c.Encoding);
	}

	[Test]
	public void parses_with_spaces_between_parameters() {
		var c = MediaType.Parse($"{ContentType.Json}; charset=woftam");

		Assert.AreEqual("application", c.Type);
		Assert.AreEqual("json", c.Subtype);
		Assert.AreEqual(ContentType.Json, c.Range);
		Assert.IsTrue(c.EncodingSpecified);
		Assert.IsNull(c.Encoding);
	}

	[Test]
	public void parses_upper_case_parameters() {
		var c = MediaType.Parse($"{ContentType.Json}; charset=UTF-8");

		Assert.AreEqual("application", c.Type);
		Assert.AreEqual("json", c.Subtype);
		Assert.AreEqual(ContentType.Json, c.Range);
		Assert.IsTrue(c.EncodingSpecified);
		Assert.AreEqual(Helper.UTF8NoBom, c.Encoding);
	}
}
