// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Transport.Http.EntityManagement;

namespace EventStore.Core.Services.Transport.Http;

public class UriToActionMatch(
	UriTemplateMatch templateMatch,
	ControllerAction controllerAction,
	Func<HttpEntityManager, UriTemplateMatch, RequestParams> requestHandler) {
	public readonly UriTemplateMatch TemplateMatch = templateMatch;
	public readonly ControllerAction ControllerAction = controllerAction;
	public readonly Func<HttpEntityManager, UriTemplateMatch, RequestParams> RequestHandler = requestHandler;
}
