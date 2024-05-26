using System.Security.Cryptography;
using System.Text;
using static System.Array;

namespace EventStore.Streaming;

/// <summary>
/// Generates deterministic GUIDs based on a namespace and input.
/// This is useful for generating GUIDs from a source identifier whereby if the identifier is regenerated
/// from the same input, the same GUID will be generated. This is essential to support idempotent handling.
///
/// This object lifecycle should be singleton with respect to lifetime of the hosting application.
/// </summary>
[PublicAPI]
internal class DeterministicGuidGenerator {
	/// <summary>
	/// Creates a new instance of <see cref="DeterministicGuidGenerator"/>.
	/// </summary>
	/// <param name="guidNamespace">
	/// A namespace that ensure uniqueness across applications / services. An application
	/// should supply a namespace value that is long term stable.
	/// </param>
	public DeterministicGuidGenerator(Guid guidNamespace) {
		NamespaceBytes = guidNamespace.ToByteArray();
		SwapByteOrder(NamespaceBytes);
	}
	
	byte[] NamespaceBytes { get; }

	/// <summary>
	/// Generates a deterministic GUID based on the namespace and input.
	/// </summary>
	/// <param name="input">
	/// The input to generate the GUID from.
	/// </param>
	/// <returns>
	/// A deterministic GUID.
	/// </returns>
	public Guid Generate(IEnumerable<byte> input) {
		byte[] hash;

		var inputBuffer = input.ToArray();

		using (var algorithm = SHA1.Create()) {
			algorithm.TransformBlock(NamespaceBytes, 0, NamespaceBytes.Length, null, 0);
			algorithm.TransformFinalBlock(inputBuffer, 0, inputBuffer.Length);

			hash = algorithm.Hash!;
		}

		var newGuid = new byte[16];

		Copy(hash, 0, newGuid, 0, 16);

		newGuid[6] = (byte)((newGuid[6] & 0x0F) | (5 << 4));
		newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80);

		SwapByteOrder(newGuid);

		return new(newGuid);
	}

	static void SwapByteOrder(byte[] guid) {
		SwapBytes(guid, 0, 3);
		SwapBytes(guid, 1, 2);
		SwapBytes(guid, 4, 5);
		SwapBytes(guid, 6, 7);
	}

	static void SwapBytes(byte[] guid, int left, int right) => 
		(guid[left], guid[right]) = (guid[right], guid[left]);
}

/// <summary>
/// Extension methods for <see cref="DeterministicGuidGenerator"/>.
/// </summary>
internal static class DeterministicGuidGeneratorExtensions {
	/// <summary>
	/// Generates a deterministic GUID by concatenating the strings with a "-" and converting to UTF8 byte array.
	/// </summary>
	/// <param name="generator"></param>
	/// <param name="input"></param>
	/// <returns></returns>
	public static Guid Generate(this DeterministicGuidGenerator generator, params string[] input)
		=> generator.Generate(Encoding.UTF8.GetBytes(string.Join("-", input)));
}

internal static class UniversalUniqueIdentifiers {
	static readonly DeterministicGuidGenerator Generator = new(Guid.Parse("c0d5e84e-0e3e-4d3f-8f6e-1d5e4f3e4c5d"));
	
	public static Guid GenerateFrom(params string[] input) => Generator.Generate(input);
}