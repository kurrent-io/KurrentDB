using System.Globalization;
using System.Text.Json;
using EventStore.Streaming.Schema;

namespace EventStore.Streaming;

[PublicAPI]
public class Headers : Dictionary<string, string?> {
    public Headers() : base(StringComparer.Ordinal) { }
    
    public Headers(Dictionary<string, string?> dictionary) : base(dictionary, StringComparer.Ordinal) { }

    public new Headers Add(string key, string? value) {
		try {
			base.Add(key, value);
			return this;
		}
		catch (Exception ex) {
			throw new($"Failed to add header [{key}]", ex);
		}
	}
	
	public Headers Set(string key, string? value) {
		try {
			base[key] = value;
			return this;
		}
		catch (Exception ex) {
			throw new($"Failed to set header [{key}]", ex);
		}
	}
	
	public Headers Add(string key, Guid value)   => Add(key, value.ToString());
	public Headers Add(string key, int value)    => Add(key, value.ToString());
	public Headers Add(string key, long value)   => Add(key, value.ToString());
	public Headers Add(string key, short value)  => Add(key, value.ToString());
	public Headers Add(string key, double value) => Add(key, value.ToString(CultureInfo.InvariantCulture));
	
	public static ReadOnlyMemory<byte> Encode(Headers headers) => 
		JsonSerializer.SerializeToUtf8Bytes(headers);
	
	public static Headers Decode(ReadOnlyMemory<byte> bytes) {
		if (bytes.IsEmpty)
			return new();
		
		try {
			return JsonSerializer.Deserialize<Headers>(bytes.Span) ?? new Headers();
		}
		catch (Exception) {
			return new();
		}
	}
	
	public Headers WithSchemaInfo(SchemaInfo schemaInfo) {
		schemaInfo.InjectIntoHeaders(this);
		return this;
	}
}