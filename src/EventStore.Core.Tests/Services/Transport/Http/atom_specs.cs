// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Xml;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Atom;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Http;

[TestFixture]
public class feed_element_must {
	private const string FeedUrl = "http://127.0.0.1/streams/test";
	private FeedElement _feed;
	private XmlWriter _writer;

	[SetUp]
	public void SetUp() {
		_feed = new FeedElement();
		_writer = XmlWriter.Create(new MemoryStream());
	}

	[TearDown]
	public void TearDown() {
		_writer.Close();
	}

	[Test]
	public void have_non_empty_title() {
		//_feed.SetTitle("Event stream 'test'");
		_feed.SetId(FeedUrl);
		_feed.SetUpdated(DateTime.UtcNow);
		_feed.SetAuthor(AtomSpecs.Author);
		_feed.AddLink("self", FeedUrl, null);

		Assert.Throws<ArgumentNullException>(() => _feed.SetTitle(null));
		Assert.Throws<AtomSpecificationViolationException>(() => _feed.WriteXml(_writer));
	}

	[Test]
	public void have_non_empty_id() {
		_feed.SetTitle("Event stream 'test'");
		//_feed.SetId(FeedUrl);
		_feed.SetUpdated(DateTime.UtcNow);
		_feed.SetAuthor(AtomSpecs.Author);
		_feed.AddLink("self", FeedUrl, null);

		Assert.Throws<ArgumentNullException>(() => _feed.SetId(null));
		Assert.Throws<AtomSpecificationViolationException>(() => _feed.WriteXml(_writer));
	}

	[Test]
	public void have_non_empty_updated() {
		_feed.SetTitle("Event stream 'test'");
		_feed.SetId(FeedUrl);
		//_feed.SetUpdated(DateTime.UtcNow);
		_feed.SetAuthor(AtomSpecs.Author);
		_feed.AddLink("self", FeedUrl, null);

		Assert.Throws<AtomSpecificationViolationException>(() => _feed.WriteXml(_writer));
	}

	[Test]
	public void format_dates_in_RFC_3339() {
		_feed.SetTitle("Event stream 'test'");
		_feed.SetId(FeedUrl);
		_feed.SetUpdated(DateTime.UtcNow);
		_feed.SetAuthor(AtomSpecs.Author);
		_feed.AddLink("self", FeedUrl, null);

		Assert.DoesNotThrow(() => _feed.WriteXml(_writer));
	}

	[Test]
	public void be_treated_as_valid_with_empty_entries_list() {
		_feed.SetTitle("Event stream 'test'");
		_feed.SetId(FeedUrl);
		_feed.SetUpdated(DateTime.UtcNow);
		_feed.SetAuthor(AtomSpecs.Author);
		_feed.AddLink("self", FeedUrl, null);

		Assert.DoesNotThrow(() => _feed.WriteXml(_writer));
	}

	[Test]
	public void have_non_empty_author() {
		_feed.SetTitle("Event stream 'test'");
		_feed.SetId(FeedUrl);
		_feed.SetUpdated(DateTime.UtcNow);
		//_feed.SetAuthor(AtomSpecs.Author);
		_feed.AddLink("self", FeedUrl, null);

		Assert.Throws<ArgumentNullException>(() => _feed.SetAuthor(null));
		Assert.Throws<AtomSpecificationViolationException>(() => _feed.WriteXml(_writer));
	}

	[Test]
	public void have_at_least_one_link() {
		_feed.SetTitle("Event stream 'test'");
		_feed.SetId(FeedUrl);
		_feed.SetUpdated(DateTime.UtcNow);
		_feed.SetAuthor(AtomSpecs.Author);
		//_feed.AddLink("self", FeedUrl, null);

		Assert.Throws<AtomSpecificationViolationException>(() => _feed.WriteXml(_writer));
	}
}

[TestFixture]
public class entry_element_must {
	[Test]
	public void have_all_fields_filled() {
		var writer = XmlWriter.Create(new MemoryStream());

		var entry = new EntryElement();
		Assert.Throws<AtomSpecificationViolationException>(() => entry.WriteXml(writer));

		entry.SetTitle("test #0");
		Assert.Throws<AtomSpecificationViolationException>(() => entry.WriteXml(writer));

		entry.SetId("guid");
		Assert.Throws<AtomSpecificationViolationException>(() => entry.WriteXml(writer));

		entry.SetUpdated(DateTime.UtcNow);
		Assert.Throws<AtomSpecificationViolationException>(() => entry.WriteXml(writer));

		entry.SetAuthor(AtomSpecs.Author);
		Assert.Throws<AtomSpecificationViolationException>(() => entry.WriteXml(writer));

		entry.SetSummary("Entry #0");

		Assert.DoesNotThrow(() => entry.WriteXml(writer));
		writer.Close();
	}
}

[TestFixture]
public class link_element_must {
	[Test]
	public void have_href_attribute() {
		var link = new LinkElement(null);
		var writer = XmlWriter.Create(new MemoryStream());

		Assert.Throws<AtomSpecificationViolationException>(() => link.WriteXml(writer));
		writer.Close();
	}
}

[TestFixture]
public class person_element_must {
	[Test]
	public void have_exactly_one_name_attribute() {
		var person = new PersonElement(null);
		var writer = XmlWriter.Create(new MemoryStream());

		Assert.Throws<AtomSpecificationViolationException>(() => person.WriteXml(writer));
		writer.Close();
	}
}

[TestFixture]
public class service_document_must {
	[Test]
	public void have_at_least_one_workspace() {
		var writer = XmlWriter.Create(new MemoryStream());
		var doc = new ServiceDocument();

		Assert.Throws<AtomSpecificationViolationException>(() => doc.WriteXml(writer));
		writer.Close();
	}
}

[TestFixture]
public class workspace_must {
	[Test]
	public void contain_title() {
		var writer = XmlWriter.Create(new MemoryStream());
		var workspace = new WorkspaceElement();

		Assert.Throws<AtomSpecificationViolationException>(() => workspace.WriteXml(writer));
		writer.Close();
	}
}

[TestFixture]
public class collection_element_must {
	private CollectionElement _collection;
	private XmlWriter _writer;

	[SetUp]
	public void SetUp() {
		_writer = XmlWriter.Create(new MemoryStream());
		_collection = new CollectionElement();
	}

	[TearDown]
	public void TearDown() {
		_writer.Close();
	}

	[Test]
	public void contain_title() {
		//_collection.SetTitle("title");
		_collection.SetUri("http://127.0.0.1/streams/test");
		_collection.AddAcceptType(ContentType.Atom);

		Assert.Throws<AtomSpecificationViolationException>(() => _collection.WriteXml(_writer));
	}

	[Test]
	public void contain_uri() {
		_collection.SetTitle("title");
		//_collection.SetUri("http://127.0.0.1/streams/test");
		_collection.AddAcceptType(ContentType.Atom);

		Assert.Throws<AtomSpecificationViolationException>(() => _collection.WriteXml(_writer));
	}
}

[TestFixture]
public class accept_element_must {
	[Test]
	public void contain_value() {
		var writer = XmlWriter.Create(new MemoryStream());
		var accept = new AcceptElement(null);

		Assert.Throws<AtomSpecificationViolationException>(() => accept.WriteXml(writer));
		writer.Close();
	}
}
