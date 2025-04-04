// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Text;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Common;
using EventStore.Core.Tests.ClientAPI.Helpers;

namespace EventStore.Core.Tests.Http.Streams;

public abstract class SpecificationWithLinkToToMaxCountDeletedEvents<TLogFormat, TStreamId>
	: HttpBehaviorSpecification<TLogFormat, TStreamId> {
	protected string LinkedStreamName;
	protected string DeletedStreamName;

	protected override async Task Given() {
		var creds = DefaultData.AdminCredentials;
		DeletedStreamName = Guid.NewGuid().ToString();
		LinkedStreamName = Guid.NewGuid().ToString();
		using (var conn = TestConnection.Create(_node.TcpEndPoint)) {
			await conn.ConnectAsync();
			await conn.AppendToStreamAsync(DeletedStreamName, ExpectedVersion.Any, creds,
					new EventData(Guid.NewGuid(), "testing1", true, Encoding.UTF8.GetBytes("{'foo' : 4}"),
						new byte[0]));
			await conn.SetStreamMetadataAsync(DeletedStreamName, ExpectedVersion.Any,
				new StreamMetadata(2, null, null, null, null));
			await conn.AppendToStreamAsync(DeletedStreamName, ExpectedVersion.Any, creds,
					new EventData(Guid.NewGuid(), "testing2", true, Encoding.UTF8.GetBytes("{'foo' : 4}"),
						new byte[0]));
			await conn.AppendToStreamAsync(DeletedStreamName, ExpectedVersion.Any, creds,
					new EventData(Guid.NewGuid(), "testing3", true, Encoding.UTF8.GetBytes("{'foo' : 4}"),
						new byte[0]));
			await conn.AppendToStreamAsync(LinkedStreamName, ExpectedVersion.Any, creds,
				new EventData(Guid.NewGuid(), SystemEventTypes.LinkTo, false,
					Encoding.UTF8.GetBytes("0@" + DeletedStreamName), new byte[0]));
		}
	}
}
