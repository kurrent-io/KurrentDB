// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using EventStore.Common.Utils;

namespace EventStore.Transport.Http.Codecs;

public class TextCodec : ICodec {
	public string ContentType {
		get { return Http.ContentType.PlainText; }
	}

	public Encoding Encoding {
		get { return Helper.UTF8NoBom; }
	}

	public bool HasEventIds {
		get { return false; }
	}

	public bool HasEventTypes {
		get { return false; }
	}

	public bool CanParse(MediaType format) {
		return format != null && format.Matches(ContentType, Encoding);
	}

	public bool SuitableForResponse(MediaType component) {
		return component.Type == "*"
		       || (string.Equals(component.Type, "text", StringComparison.OrdinalIgnoreCase)
		           && (component.Subtype == "*"
		               || string.Equals(component.Subtype, "plain", StringComparison.OrdinalIgnoreCase)));
	}

	public T From<T>(string text) {
		throw new NotSupportedException();
	}

	public string To<T>(T value) {
		return ((object)value) != null ? value.ToString() : null;
	}
}
