using System.Collections.Generic;
using System.Linq;

namespace EventStore.Core.Services.Transport.Grpc {
	public struct HashCode {
		private readonly int _value;

		private HashCode(int value) {
			_value = value;
		}

		public static readonly HashCode Hash = default;

		public readonly HashCode Combine<T>(T? value) where T : struct => Combine(value ?? default);

		public readonly HashCode Combine<T>(T value) where T : struct {
			unchecked {
				return new HashCode((_value * 397) ^ value.GetHashCode());
			}
		}

		public readonly HashCode Combine(string value) {
			unchecked {
				return new HashCode((_value * 397) ^ (value?.GetHashCode() ?? 0));
			}
		}

		public readonly HashCode Combine<T>(IEnumerable<T> values) where T : struct =>
			values.Aggregate(Hash, (previous, value) => previous.Combine(value));

		public readonly HashCode Combine(IEnumerable<string> values) =>
			values.Aggregate(Hash, (previous, value) => previous.Combine(value));

		public static implicit operator int(HashCode value) => value._value;
	}
}
