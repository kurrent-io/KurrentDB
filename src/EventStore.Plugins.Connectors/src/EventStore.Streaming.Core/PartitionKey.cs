using System.Text;
using static System.String;

namespace EventStore.Streaming;

[PublicAPI]
public readonly record struct PartitionKey {
	public static readonly PartitionKey None = new(ReadOnlyMemory<byte>.Empty, Empty);

	PartitionKey(ReadOnlyMemory<byte> encoded, string decoded) {
		Encoded = encoded;
		Decoded = decoded;
	}

	public ReadOnlyMemory<byte> Encoded { get; private init; }
	public string               Decoded { get; private init; }

	public bool IsBytes => Decoded.Length == 0;
    
	public override string ToString() => Decoded;
	
	public static PartitionKey From(ReadOnlyMemory<byte> partitionKey) {
		if (partitionKey.IsEmpty) return None;
        
		var output = new Span<char>();

		return Encoding.UTF8.TryGetChars(partitionKey.Span, output, out _)
			? new(partitionKey, output.ToString())
			: new(partitionKey, Empty);
	}
	
	public static PartitionKey From(string? partitionKey) {
		Ensure.NotNullOrWhiteSpace(partitionKey);
		return new(Encoding.UTF8.GetBytes(partitionKey!), partitionKey!);
	}

	public static PartitionKey From(ulong partitionKey) => From(partitionKey.ToString());
	public static PartitionKey From(long partitionKey)  => From(partitionKey.ToString());
	public static PartitionKey From(uint partitionKey)  => From(partitionKey.ToString());
	public static PartitionKey From(int partitionKey)   => From(partitionKey.ToString());
	public static PartitionKey From(Guid partitionKey)  => From(partitionKey.ToString());
	
	public static implicit operator string(PartitionKey _)               => _.Decoded;
	public static implicit operator byte[](PartitionKey _)               => _.Encoded.ToArray();
	public static implicit operator ReadOnlySpan<byte>(PartitionKey _)   => _.Encoded.Span;
	public static implicit operator ReadOnlyMemory<byte>(PartitionKey _) => _.Encoded.ToArray();

	public static implicit operator PartitionKey(string _) => From(_);
	public static implicit operator PartitionKey(Guid _)   => From(_.ToString());
	
	public static bool operator ==(PartitionKey? left, PartitionKey? right) => Equals(left, right);
	public static bool operator !=(PartitionKey? left, PartitionKey? right) => !Equals(left, right);

	public bool Equals(PartitionKey other) => Decoded == other.Decoded;

	public override int GetHashCode() => Decoded.GetHashCode();
}