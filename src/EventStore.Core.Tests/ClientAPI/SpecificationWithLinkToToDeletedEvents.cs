using System;
using System.Text;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Common;

namespace EventStore.Core.Tests.ClientAPI {
	public abstract class SpecificationWithLinkToToDeletedEvents<TLogFormat, TStreamId>
		: SpecificationWithMiniNode<TLogFormat, TStreamId> {
		protected string LinkedStreamName;
		protected string DeletedStreamName;

		protected override async Task Given() {
			var creds = DefaultData.AdminCredentials;
			LinkedStreamName = Guid.NewGuid().ToString();
			DeletedStreamName = Guid.NewGuid().ToString();
			await _conn.AppendToStreamAsync(DeletedStreamName, ExpectedVersion.Any, creds,
					new EventData(Guid.NewGuid(), "testing", true, Encoding.UTF8.GetBytes("{'foo' : 4}"), new byte[0]));
			await _conn.AppendToStreamAsync(LinkedStreamName, ExpectedVersion.Any, creds,
				new EventData(Guid.NewGuid(), SystemEventTypes.LinkTo, false,
					Encoding.UTF8.GetBytes("0@" + DeletedStreamName), new byte[0]));
			await _conn.DeleteStreamAsync(DeletedStreamName, ExpectedVersion.Any);
		}
	}
}
