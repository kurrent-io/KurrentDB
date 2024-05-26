using System.Diagnostics;

namespace EventStore.Streaming;

internal static class TypeExtensions {
	static readonly Type MissingType = Type.Missing.GetType();
	
	[DebuggerStepThrough]
	public static bool IsMissing(this Type source) => source == MissingType;
}