// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using EventStore.Common.Utils;

namespace EventStore.Transport.Http.Codecs;

public class CustomCodec : ICodec {
	public ICodec BaseCodec {
		get { return _codec; }
	}

	public string ContentType {
		get { return _contentType; }
	}

	public Encoding Encoding {
		get { return _encoding; }
	}

	public bool HasEventIds {
		get { return _hasEventIds; }
	}

	public bool HasEventTypes {
		get { return _hasEventTypes; }
	}

	private readonly ICodec _codec;
	private readonly string _contentType;
	private readonly string _type;
	private readonly string _subtype;
	private readonly Encoding _encoding;
	private readonly bool _hasEventIds;
	private readonly bool _hasEventTypes;

	internal CustomCodec(ICodec codec, string contentType, Encoding encoding, bool hasEventIds,
		bool hasEventTypes) {
		Ensure.NotNull(codec, "codec");
		Ensure.NotNull(contentType, "contentType");
		_hasEventTypes = hasEventTypes;
		_hasEventIds = hasEventIds;
		_codec = codec;
		_contentType = contentType;
		_encoding = encoding;
		var parts = contentType.Split(new[] {'/'}, 2);
		if (parts.Length != 2)
			throw new ArgumentException("contentType");
		_type = parts[0];
		_subtype = parts[1];
	}

	public bool CanParse(MediaType format) {
		return format != null && format.Matches(ContentType, Encoding);
	}

	public bool SuitableForResponse(MediaType component) {
		return component.Type == "*"
		       || (string.Equals(component.Type, _type, StringComparison.OrdinalIgnoreCase)
		           && (component.Subtype == "*"
		               || string.Equals(component.Subtype, _subtype, StringComparison.OrdinalIgnoreCase)));
	}

	public T From<T>(string text) {
		return _codec.From<T>(text);
	}

	public string To<T>(T value) {
		return _codec.To(value);
	}
}
