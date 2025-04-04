using EventStore.Core.Index.Hashes;

namespace EventStore.Core.Tests.Index {
	public class FakeIndexHasher : IHasher<string> {
		public uint Hash(byte[] data) {
			return (uint)data.Length;
		}

		public uint Hash(string s) {
			return uint.Parse(s);
		}

		public uint Hash(byte[] data, int offset, uint len, uint seed) {
			return (uint)data.Length;
		}
	}
}
