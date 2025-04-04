using System;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Core.Services.RequestManager;
using EventStore.Core.Services.RequestManager.Managers;

namespace EventStore.Core.Tests.Services.RequestManagement {
	public class FakeRequestManager : RequestManagerBase {
		public FakeRequestManager(
				IPublisher publisher,
				TimeSpan timeout,
				IEnvelope clientResponseEnvelope,
				Guid internalCorrId,
				Guid clientCorrId,
				long expectedVersion,
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
				 expectedVersion,
				 commitSource,
				 prepareCount,
				 transactionId,
				 waitForCommit) { }
		protected override Message WriteRequestMsg => throw new NotImplementedException();
		protected override Message ClientSuccessMsg => throw new NotImplementedException();
		protected override Message ClientFailMsg => throw new NotImplementedException();
	}
}
