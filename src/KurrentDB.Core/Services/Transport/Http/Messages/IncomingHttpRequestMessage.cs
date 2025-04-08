// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Transport.Http.EntityManagement;

namespace KurrentDB.Core.Services.Transport.Http.Messages;

[DerivedMessage(CoreMessage.Http)]
public partial class IncomingHttpRequestMessage(IHttpService httpService, HttpEntity entity, IPublisher nextStagePublisher) : Message {
	public readonly IPublisher NextStagePublisher = nextStagePublisher;
	public readonly IHttpService HttpService = httpService;
	public readonly HttpEntity Entity = entity;
}
