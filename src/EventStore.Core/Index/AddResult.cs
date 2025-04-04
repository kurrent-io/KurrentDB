namespace EventStore.Core.Index {
	public class AddResult {
		public readonly IndexMap NewMap;
		public readonly bool CanMergeAny;

		public AddResult(IndexMap newMap, bool canMergeAny) {
			NewMap = newMap;
			CanMergeAny = canMergeAny;
		}
	}
}
