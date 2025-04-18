// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Plugins.Authorization;
using KurrentDB.Common.Utils;
using KurrentDB.Transport.Http;

namespace KurrentDB.Core.Services.Transport.Http;

public class ControllerAction {
	public readonly string UriTemplate;
	public readonly string HttpMethod;
	public readonly Func<UriTemplateMatch, Operation> Operation;
	public readonly ICodec[] SupportedRequestCodecs;
	public readonly ICodec[] SupportedResponseCodecs;
	public readonly ICodec DefaultResponseCodec;

	public ControllerAction(string uriTemplate,
		string httpMethod,
		ICodec[] requestCodecs,
		ICodec[] responseCodecs,
		Operation operation) : this(uriTemplate, httpMethod, requestCodecs, responseCodecs, _ => operation) {
	}

	public ControllerAction(
		string uriTemplate,
		string httpMethod,
		ICodec[] requestCodecs,
		ICodec[] responseCodecs,
		Func<UriTemplateMatch, Operation> operation) {
		UriTemplate = Ensure.NotNull(uriTemplate);
		HttpMethod = Ensure.NotNull(httpMethod);
		Operation = operation;
		SupportedRequestCodecs = Ensure.NotNull(requestCodecs);
		SupportedResponseCodecs = Ensure.NotNull(responseCodecs);
		DefaultResponseCodec = responseCodecs.Length > 0 ? responseCodecs[0] : null;
	}

	public bool Equals(ControllerAction other) {
		if (ReferenceEquals(null, other))
			return false;
		if (ReferenceEquals(this, other))
			return true;
		return Equals(other.UriTemplate, UriTemplate) && Equals(other.HttpMethod, HttpMethod);
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj))
			return false;
		if (ReferenceEquals(this, obj))
			return true;
		if (obj.GetType() != typeof(ControllerAction))
			return false;
		return Equals((ControllerAction)obj);
	}

	public override int GetHashCode() {
		unchecked {
			return (UriTemplate.GetHashCode() * 397) ^ HttpMethod.GetHashCode();
		}
	}

	public override string ToString() {
		return string.Format("UriTemplate: {0}, HttpMethod: {1}, SupportedCodecs: {2}, DefaultCodec: {3}",
			UriTemplate,
			HttpMethod,
			SupportedResponseCodecs,
			DefaultResponseCodec);
	}
}
