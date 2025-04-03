// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using EventStore.Common.Utils;

namespace EventStore.Transport.Http.Atom;

public class ServiceDocument : IXmlSerializable {
	public List<WorkspaceElement> Workspaces { get; set; }

	public ServiceDocument() {
		Workspaces = new List<WorkspaceElement>();
	}

	public void AddWorkspace(WorkspaceElement workspace) {
		Ensure.NotNull(workspace, "workspace");
		Workspaces.Add(workspace);
	}

	public XmlSchema GetSchema() {
		return null;
	}

	public void ReadXml(XmlReader reader) {
		throw new NotImplementedException();
	}

	public void WriteXml(XmlWriter writer) {
		if (Workspaces.Count == 0)
			ThrowHelper.ThrowSpecificationViolation(
				"An app:service element MUST contain one or more app:workspace elements.");

		writer.WriteStartElement("service", AtomSpecs.AtomPubV1Namespace);
		writer.WriteAttributeString("xmlns", "atom", null, AtomSpecs.AtomV1Namespace);
		Workspaces.ForEach(w => w.WriteXml(writer));

		writer.WriteEndElement();
	}
}

public class WorkspaceElement : IXmlSerializable {
	public string Title { get; set; }
	public List<CollectionElement> Collections { get; set; }

	public WorkspaceElement() {
		Collections = new List<CollectionElement>();
	}

	public void SetTitle(string title) {
		Ensure.NotNull(title, "title");
		Title = title;
	}

	public void AddCollection(CollectionElement collection) {
		Ensure.NotNull(collection, "collection");
		Collections.Add(collection);
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
				"The app:workspace element MUST contain one 'atom:title' element");

		writer.WriteStartElement("workspace");

		writer.WriteElementString("atom", "title", AtomSpecs.AtomV1Namespace, Title);
		Collections.ForEach(c => c.WriteXml(writer));

		writer.WriteEndElement();
	}
}

public class CollectionElement : IXmlSerializable {
	public string Title { get; set; }
	public string Uri { get; set; }

	public List<AcceptElement> Accepts { get; set; }

	public CollectionElement() {
		Accepts = new List<AcceptElement>();
	}

	public void SetTitle(string title) {
		Ensure.NotNull(title, "title");
		Title = title;
	}

	public void SetUri(string uri) {
		Ensure.NotNull(uri, "uri");
		Uri = uri;
	}

	public void AddAcceptType(string type) {
		Ensure.NotNull(type, "type");
		Accepts.Add(new AcceptElement(type));
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
				"The app: collection element MUST contain one atom:title element.");
		if (string.IsNullOrEmpty(Uri))
			ThrowHelper.ThrowSpecificationViolation(
				"The app:collection element MUST contain an 'href' attribute, " +
				"whose value gives the IRI of the Collection.");

		writer.WriteStartElement("collection");
		writer.WriteAttributeString("href", Uri);
		writer.WriteElementString("atom", "title", AtomSpecs.AtomV1Namespace, Title);
		Accepts.ForEach(a => a.WriteXml(writer));

		writer.WriteEndElement();
	}
}

public class AcceptElement : IXmlSerializable {
	public string Type { get; set; }

	public AcceptElement(string type) {
		Type = type;
	}

	public XmlSchema GetSchema() {
		return null;
	}

	public void ReadXml(XmlReader reader) {
		throw new NotImplementedException();
	}

	public void WriteXml(XmlWriter writer) {
		if (string.IsNullOrEmpty(Type))
			ThrowHelper.ThrowSpecificationViolation("atom:accept element MUST contain value");
		writer.WriteElementString("accept", Type);
	}
}
