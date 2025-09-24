// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Common;

namespace KurrentDB.Core.Tests.ClientAPI;

public abstract class SpecificationWithLinkToToDeletedEvents<TLogFormat, TStreamId>
	: SpecificationWithMiniNode<TLogFormat, TStreamId> {
	protected string LinkedStreamName;
	protected string DeletedStreamName;

	protected override async Task Given() {
		var creds = DefaultData.AdminCredentials;
		LinkedStreamName = Guid.NewGuid().ToString();
		DeletedStreamName = Guid.NewGuid().ToString();
		await _conn.AppendToStreamAsync(DeletedStreamName, ExpectedVersion.Any, creds,
				new EventData(Guid.NewGuid(), "testing", true, Encoding.UTF8.GetBytes("{'foo' : 4}"), []));
		await _conn.AppendToStreamAsync(LinkedStreamName, ExpectedVersion.Any, creds,
			new EventData(Guid.NewGuid(), SystemEventTypes.LinkTo, false,
				Encoding.UTF8.GetBytes("0@" + DeletedStreamName), []));
		await _conn.DeleteStreamAsync(DeletedStreamName, ExpectedVersion.Any);
	}
}
