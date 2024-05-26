// ReSharper disable InconsistentNaming

using static System.Text.Encoding;

namespace EventStore.Streaming;

public delegate uint GenerateHash(string input);

[PublicAPI]
public class HashGenerators {
	const uint DefaultSeed = 0;
	
	public static GenerateHash MurmurHash3Espresso => input => {
		ReadOnlySpan<byte> inputSpan = UTF8.GetBytes(input);
		return MurmurHash.MurmurHash3.Hash32(ref inputSpan, DefaultSeed);
	};
	
	public static GenerateHash MurmurHash3 => 
		input => HashDepot.MurmurHash3.Hash32((ReadOnlySpan<byte>) UTF8.GetBytes(input), DefaultSeed);
	
	public static GenerateHash XXHash => 
		input => HashDepot.XXHash.Hash32((ReadOnlySpan<byte>) UTF8.GetBytes(input));
	
	public static GenerateHash Fnv1a => 
		input => HashDepot.Fnv1a.Hash32((ReadOnlySpan<byte>) UTF8.GetBytes(input));
}