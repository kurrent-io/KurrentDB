using System;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.LogV2 {
	public class LogV2PartitionManager : IPartitionManager {

		public Guid? RootId => Guid.Empty;
		public Guid? RootTypeId => Guid.Empty;

		public void Initialize() { }
	}
}
