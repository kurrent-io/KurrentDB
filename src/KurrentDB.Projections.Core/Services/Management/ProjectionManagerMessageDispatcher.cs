// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Messages;
using Serilog;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Management;

public class ProjectionManagerMessageDispatcher
	: IHandle<CoreProjectionManagementControlMessage> {
	private readonly ILogger _logger = Log.ForContext<ProjectionManager>();
	private readonly IDictionary<Guid, IPublisher> _queueMap;

	public ProjectionManagerMessageDispatcher(IDictionary<Guid, IPublisher> queueMap) {
		_queueMap = queueMap;
	}

	public void Handle(CoreProjectionManagementControlMessage message) {
		DispatchWorkerMessage(message, message.WorkerId);
	}

	private void DispatchWorkerMessage(Message message, Guid workerId) {
		IPublisher worker;
		if (_queueMap.TryGetValue(workerId, out worker))
			worker.Publish(message);
		else
			_logger.Information("Cannot find a worker with ID: {workerId}", workerId);
	}
}
