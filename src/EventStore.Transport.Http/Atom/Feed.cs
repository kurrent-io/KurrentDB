// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using EventStore.Common.Utils;
using Newtonsoft.Json;

namespace EventStore.Transport.Http.Atom;

public class FeedElement : IXmlSerializable {
	public string Title { get; set; }
	public string Id { get; set; }
	public string Updated { get; set; }
	public string StreamId { get; set; }
	public PersonElement Author { get; set; }
	public bool HeadOfStream { get; set; }
	public string SelfUrl { get; set; }
	public string ETag { get; set; }

	public List<LinkElement> Links { get; set; }
	public List<EntryElement> Entries { get; set; }

	public FeedElement() {
		Links = new List<LinkElement>();
		Entries = new List<EntryElement>();
	}

	public void SetTitle(string title) {
		Ensure.NotNull(title, "title");
		Title = title;
	}

	public void SetId(string id) {
		Ensure.NotNull(id, "id");
		Id = id;
	}

	public void SetUpdated(DateTime dateTime) {
		Updated = XmlConvert.ToString(dateTime, XmlDateTimeSerializationMode.Utc);
	}

	public void SetAuthor(string name) {
		Ensure.NotNull(name, "name");
		Author = new PersonElement(name);
	}

	public void SetHeadOfStream(bool headOfStream) {
		this.HeadOfStream = headOfStream;
	}

	public void SetSelfUrl(string self) {
		this.SelfUrl = self;
	}

	public void SetETag(string etag) {
		this.ETag = etag;
	}

	public void AddLink(string relation, string uri, string contentType = null) {
		Ensure.NotNull(uri, "uri");
		Links.Add(new LinkElement(uri, relation, contentType));
	}

	public void AddEntry(EntryElement entry) {
		Ensure.NotNull(entry, "entry");
		Entries.Add(entry);
	}

	public XmlSchema GetSchema() {
		return null;
	}

	public void ReadXml(XmlReader reader) {
		throw new NotImplementedException();
	}

	public void WriteXml(XmlWriter writer) {
		if (string.IsNullOrEmpty(Title))
			ThrowHelper.ThrowSpecificationViolation(
				"atom:feed elements MUST contain exactly one atom:title element.");
		if (string.IsNullOrEmpty(Id))
			ThrowHelper.ThrowSpecificationViolation("atom:feed elements MUST contain exactly one atom:id element.");
		if (string.IsNullOrEmpty(Updated))
			ThrowHelper.ThrowSpecificationViolation(
				"atom:feed elements MUST contain exactly one atom:updated element.");
		if (Author == null)
			ThrowHelper.ThrowSpecificationViolation(
				"atom:feed elements MUST contain one or more atom:author elements");
		if (Links.Count == 0)
			ThrowHelper.ThrowSpecificationViolation(
				"atom:feed elements SHOULD contain one atom:link element with a "
				+ "rel attribute value of 'self'. This is the preferred URI for retrieving Atom Feed Documents representing this Atom feed.");

		writer.WriteStartElement("feed", AtomSpecs.AtomV1Namespace);

		writer.WriteElementString("title", AtomSpecs.AtomV1Namespace, Title);
		writer.WriteElementString("id", AtomSpecs.AtomV1Namespace, Id);
		writer.WriteElementString("updated", AtomSpecs.AtomV1Namespace, Updated);
		Author.WriteXml(writer);

		Links.ForEach(link => link.WriteXml(writer));
		Entries.ForEach(entry => entry.WriteXml(writer, usePrefix: false));

		writer.WriteEndElement();
	}
}

public class EntryElement : IXmlSerializable {
	private object _content;
	public string Title { get; set; }
	public string Id { get; set; }
	public string Updated { get; set; }
	public PersonElement Author { get; set; }
	public string Summary { get; set; }
	public int? RetryCount { get; set; }

	public object Content {
		get { return _content; }
		set { throw new NotSupportedException(); }
	}

	public List<LinkElement> Links { get; set; }

	public EntryElement() {
		Links = new List<LinkElement>();
	}

	public void SetTitle(string title) {
		Ensure.NotNull(title, "title");
		Title = title;
	}

	public void SetId(string id) {
		Ensure.NotNull(id, "id");
		Id = id;
	}

	public void SetUpdated(DateTime dateTime) {
		Updated = XmlConvert.ToString(dateTime, XmlDateTimeSerializationMode.Utc);
	}

	public void SetAuthor(string name) {
		Ensure.NotNull(name, "name");
		Author = new PersonElement(name);
	}

	public void SetSummary(string summary) {
		Ensure.NotNull(summary, "summary");
		Summary = summary;
	}

	public void AddLink(string relation, string uri, string type = null) {
		Ensure.NotNull(uri, "uri");
		Links.Add(new LinkElement(uri, relation, type));
	}

	public void AddRetryCount(int value) {
		RetryCount = value;
	}

	public XmlSchema GetSchema() {
		return null;
	}

	public void ReadXml(XmlReader reader) {
		reader.ReadStartElement("entry");

		Title = reader.ReadElementString("title");
		Id = reader.ReadElementString("id");
		Updated = reader.ReadElementString("updated");
		Author.ReadXml(reader);
		Summary = reader.ReadElementString("summary");
		Links.ForEach(l => l.ReadXml(reader));

		reader.ReadEndElement();
	}

	public void WriteXml(XmlWriter writer) {
		WriteXml(writer, usePrefix: true);
	}

	public void WriteXml(XmlWriter writer, bool usePrefix) {
		if (string.IsNullOrEmpty(Title))
			ThrowHelper.ThrowSpecificationViolation(
				"atom:entry elements MUST contain exactly one atom:title element.");
		if (string.IsNullOrEmpty(Id))
			ThrowHelper.ThrowSpecificationViolation(
				"atom:entry elements MUST contain exactly one atom:id element.");
		if (string.IsNullOrEmpty(Updated))
			ThrowHelper.ThrowSpecificationViolation(
				"atom:entry elements MUST contain exactly one atom:updated element.");
		if (Author == null)
			ThrowHelper.ThrowSpecificationViolation(
				"atom:entry elements MUST contain one or more atom:author elements");
		if (string.IsNullOrEmpty(Summary))
			ThrowHelper.ThrowSpecificationViolation("atom:entry elements MUST contain an atom:summary element");

		if (usePrefix)
			writer.WriteStartElement("atom", "entry", AtomSpecs.AtomV1Namespace);
		else
			writer.WriteStartElement("entry", AtomSpecs.AtomV1Namespace);

		writer.WriteElementString("title", AtomSpecs.AtomV1Namespace, Title);
		writer.WriteElementString("id", AtomSpecs.AtomV1Namespace, Id);
		writer.WriteElementString("updated", AtomSpecs.AtomV1Namespace, Updated);
		Author.WriteXml(writer);
		writer.WriteElementString("summary", AtomSpecs.AtomV1Namespace, Summary);

		if (RetryCount != null) {
			writer.WriteElementString("retryCount", RetryCount.Value.ToString());
		}

		Links.ForEach(link => link.WriteXml(writer));
		if (Content != null) {
			var serializeObject = JsonConvert.SerializeObject(Content);
			var deserializeXmlNode = JsonConvert.DeserializeXmlNode(serializeObject, "content");
			writer.WriteStartElement("content", AtomSpecs.AtomV1Namespace);
			writer.WriteAttributeString("type", ContentType.ApplicationXml);
			deserializeXmlNode.DocumentElement.WriteContentTo(writer);
			writer.WriteEndElement();
		}

		writer.WriteEndElement();
	}

	public void SetContent(object content) {
		_content = content;
	}
}

public class RichEntryElement : EntryElement {
	public Guid EventId { get; set; }
	public string EventType { get; set; }
	public long EventNumber { get; set; }
	public string Data { get; set; }
	public string MetaData { get; set; }
	public string LinkMetaData { get; set; }

	public string StreamId { get; set; }

	public bool IsJson { get; set; }

	public bool IsMetaData { get; set; }
	public bool IsLinkMetaData { get; set; }
	public bool IsRedacted { get; set; }

	public long PositionEventNumber { get; set; }

	public string PositionStreamId { get; set; }
}

public class LinkElement : IXmlSerializable {
	public string Uri { get; set; }
	public string Relation { get; set; }
	public string Type { get; set; }

	public LinkElement(string uri) : this(uri, null, null) {
	}

	public LinkElement(string uri, string relation) : this(uri, relation, null) {
	}

	public LinkElement(string uri, string relation, string type) {
		Uri = uri;
		Relation = relation;
		Type = type;
	}

	public XmlSchema GetSchema() {
		return null;
	}

	public void ReadXml(XmlReader reader) {
		reader.ReadStartElement("link");

		Uri = reader.GetAttribute("href");
		Relation = reader.GetAttribute("rel");
		Type = reader.GetAttribute("type");

		reader.ReadEndElement();
	}

	public void WriteXml(XmlWriter writer) {
		if (string.IsNullOrEmpty(Uri))
			ThrowHelper.ThrowSpecificationViolation(
				"atom:link elements MUST have an href attribute, whose value MUST be a URI reference");

		writer.WriteStartElement("link", AtomSpecs.AtomV1Namespace);
		writer.WriteAttributeString("href", Uri);

		if (Relation != null)
			writer.WriteAttributeString("rel", Relation);
		if (Type != null)
			writer.WriteAttributeString("type", Type);

		writer.WriteEndElement();
	}
}

public class PersonElement : IXmlSerializable {
	public string Name { get; set; }

	public PersonElement(string name) {
		Name = name;
	}

	public XmlSchema GetSchema() {
		return null;
	}

	public void ReadXml(XmlReader reader) {
		reader.ReadStartElement("author");
		Name = reader.ReadElementString("name");
		reader.ReadEndElement();
	}

	public void WriteXml(XmlWriter writer) {
		if (string.IsNullOrEmpty(Name))
			ThrowHelper.ThrowSpecificationViolation(
				"Person constructs MUST contain exactly one 'atom:name' element.");

		writer.WriteStartElement("author", AtomSpecs.AtomV1Namespace);
		writer.WriteElementString("name", AtomSpecs.AtomV1Namespace, Name);
		writer.WriteEndElement();
	}
}
