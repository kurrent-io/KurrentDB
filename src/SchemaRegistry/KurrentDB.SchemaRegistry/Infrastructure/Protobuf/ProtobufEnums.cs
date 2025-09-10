using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Google.Protobuf.Reflection;

namespace KurrentDB.Api.Infrastructure.Protobuf;

public class ProtobufEnums(Assembly assembly) {
	public static ProtobufEnums System { get; } = new(Assembly.GetExecutingAssembly());

	Dictionary<Type, EnumDescriptor> EnumTypes { get; } = ScanEnumDescriptors(assembly);

	static Dictionary<Type, EnumDescriptor> ScanEnumDescriptors(Assembly assembly) {
		return assembly.GetTypes()
			.Where(t => t.Name.EndsWith("Reflection") && t.IsClass)
			.Select(t => t.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static))
			.SelectMany(prop => prop?.GetValue(null) is FileDescriptor fs ? fs.EnumTypes : [])
			.ToDictionary(x => x.ClrType, x => x);
	}

	public bool TryGetEnumDescriptor(Type enumType, [MaybeNullWhen(false)] out EnumDescriptor descriptor) =>
		EnumTypes.TryGetValue(enumType, out descriptor);

	public bool TryGetEnumValueDescriptor(Type enumType, ReadOnlySpan<char> value, [MaybeNullWhen(false)] out EnumValueDescriptor descriptor) {
		descriptor = Enum.TryParse(enumType, value, true, out var enumValue) && TryGetEnumDescriptor(enumType, out var enumDescriptor)
			? enumDescriptor.FindValueByName(Enum.GetName(enumType, enumValue))
			: null;

		return descriptor is not null;
	}

	public bool TryGetEnumDescriptor<T>([MaybeNullWhen(false)] out EnumDescriptor descriptor) where T : struct, Enum =>
		TryGetEnumDescriptor(typeof(T), out descriptor);

	public bool TryGetEnumValueDescriptor<T>(ReadOnlySpan<char> value, [MaybeNullWhen(false)] out EnumValueDescriptor descriptor) where T : struct, Enum =>
		TryGetEnumValueDescriptor(typeof(T), value, out descriptor);

	public bool TryGetEnumValueDescriptor<T>(T value, [MaybeNullWhen(false)] out EnumValueDescriptor descriptor) where T : struct, Enum {
		descriptor = TryGetEnumDescriptor<T>(out var enumDescriptor) ? enumDescriptor.FindValueByName(Enum.GetName(value)) : null;
		return descriptor is not null;
	}

	public EnumDescriptor GetEnumDescriptor(Type enumType) {
		return TryGetEnumDescriptor(enumType, out var descriptor)
			? descriptor
			: throw new KeyNotFoundException($"'{enumType.FullName}' is not a protobuf enum or its descriptor was not found.");
	}

	public EnumValueDescriptor GetEnumValueDescriptor(Type enumType, ReadOnlySpan<char> value) {
		return TryGetEnumValueDescriptor(enumType, value, out var descriptor)
			? descriptor
			: throw new KeyNotFoundException($"'{value}' is not a valid value of the protobuf enum '{enumType.FullName}'.");
	}

	public EnumDescriptor GetEnumDescriptor<T>() where T : struct, Enum =>
		GetEnumDescriptor(typeof(T));

	public EnumValueDescriptor GetEnumValueDescriptor<T>(ReadOnlySpan<char> value) where T : struct, Enum =>
		GetEnumValueDescriptor(typeof(T), value);

	public EnumValueDescriptor GetEnumValueDescriptor<T>(T value) where T : struct, Enum =>
		GetEnumValueDescriptor(typeof(T), Enum.GetName(value));
}
