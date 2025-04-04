using System;

namespace EventStore.Core.LogAbstraction {
	public interface IPartitionManager {

		Guid? RootId { get; }
		Guid? RootTypeId { get; }

		void Initialize();
	}
}
