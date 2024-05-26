using EventStore.Streaming.Schema;

namespace EventStore.Streaming.Producers;

[PublicAPI]
public readonly record struct Message() {
	public static MessageBuilder Builder => new();

	public static readonly Message Empty = new();
	
	/// <summary>
	/// The message payload.
	/// </summary>
	public object Value { get; init; } = null!;

	/// <summary>
	/// The partition key of the message.
	/// </summary>
	public PartitionKey Key { get; init; } = PartitionKey.None;

	/// <summary>
	/// The message headers.
	/// </summary>
	public Headers Headers { get; init; } = new Headers { [HeaderKeys.SchemaType] = SchemaDefinitionType.Json.ToString().ToLower() };

	/// <summary>
	/// The assigned record id.
	/// </summary>
	public RecordId RecordId { get; init; } = Guid.NewGuid();

	/// <summary>
	/// The schema type of the message.
	/// </summary>
	public SchemaDefinitionType SchemaType { get; init; } = SchemaDefinitionType.Json;

	public bool IsValid => this != Empty 
	                    && Value != null 
	                    && SchemaType != SchemaDefinitionType.Undefined 
	                    && Headers.ContainsKey(HeaderKeys.SchemaType) 
	                    && (Key != PartitionKey.None && Headers.ContainsKey(HeaderKeys.PartitionKey) || Key == PartitionKey.None);
}

[PublicAPI]
public record MessageBuilder {
	Message Options { get; init; } = new();
	
	public MessageBuilder Value<T>(T value) where T : class {
		Ensure.NotNull(value);

		return this with {
			Options = Options with {
				Value = value
			}
		};
	}

	public MessageBuilder Key(PartitionKey partitionKey) {
		Ensure.NotDefault(partitionKey, PartitionKey.None);
		
		return this with {
			Options = Options with {
				Key     = partitionKey,
				Headers = Options.Headers.Set(HeaderKeys.PartitionKey, partitionKey)
			}
		};
	}

	public MessageBuilder Headers(Headers headers) {
		Ensure.NotNull(headers);

		return this with {
			Options = Options with {
				Headers = headers
			}
		};
	}

	public MessageBuilder Headers(Action<Headers> setHeaders) {
		Ensure.NotNull(setHeaders);
		return Headers(Options.Headers.With(setHeaders));
	}
	
	public MessageBuilder RecordId(Guid recordId) {
		Ensure.NotEmpty(recordId);

		return this with {
			Options = Options with {
				RecordId = recordId
			}
		};
	}
	
	public MessageBuilder WithSchemaType(SchemaDefinitionType schemaType) {
		Ensure.NotDefault(schemaType, SchemaDefinitionType.Undefined);
		
		return this with {
			Options = Options with {
				SchemaType = schemaType,
				Headers    = Options.Headers.Set(HeaderKeys.SchemaType, schemaType.ToString().ToLower())
			}
		};
	}
	
	public Message Create() {
		Ensure.NotNull(Options.Value);
		
		var messageType = Options.Value.GetType();
		
		if (Options.SchemaType == SchemaDefinitionType.Bytes 
		    && messageType != typeof(byte[]) 
		    && messageType != typeof(ReadOnlyMemory<byte>) 
		    && messageType != typeof(Memory<byte>)) {
			throw new ArgumentException($"SchemaType {SchemaDefinitionType.Bytes} requires a message type of byte[], ReadOnlyMemory<byte>, or Memory<byte>.");
		}
		
		return Options;
	}
}