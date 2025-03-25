// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.RequestManager;
using KurrentDB.Core.Services.RequestManager.Managers;

namespace KurrentDB.Core.Tests.Services.RequestManagement;

public class FakeRequestManager : RequestManagerBase {
	public FakeRequestManager(
			IPublisher publisher,
			TimeSpan timeout,
			IEnvelope clientResponseEnvelope,
			Guid internalCorrId,
			Guid clientCorrId,
			CommitSource commitSource,
			int prepareCount = 0,
			long transactionId = -1,
			bool waitForCommit = false)
		: base(
			 publisher,
			 timeout,
			 clientResponseEnvelope,
			 internalCorrId,
			 clientCorrId,
			 commitSource,
			 prepareCount,
			 transactionId,
			 waitForCommit) { }
	protected override Message WriteRequestMsg => throw new NotImplementedException();
	protected override Message ClientSuccessMsg => throw new NotImplementedException();
	protected override Message ClientFailMsg => throw new NotImplementedException();
}
