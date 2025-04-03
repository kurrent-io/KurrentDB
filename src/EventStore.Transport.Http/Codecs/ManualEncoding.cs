// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;

namespace EventStore.Transport.Http.Codecs;

public class ManualEncoding : ICodec {
	public string ContentType {
		get { throw new InvalidOperationException(); }
	}

	public Encoding Encoding {
		get { throw new InvalidOperationException(); }
	}

	public bool HasEventIds {
		get { return false; }
	}

	public bool HasEventTypes {
		get { return false; }
	}

	public bool CanParse(MediaType format) {
		return true;
	}

	public bool SuitableForResponse(MediaType component) {
		return true;
	}

	public T From<T>(string text) {
		throw new InvalidOperationException();
	}

	public string To<T>(T value) {
		throw new InvalidOperationException();
	}
}
