using EventStore.Streaming.Schema;
using static System.String;

namespace EventStore.Streaming.Producers;

public interface ISendRequest {
    public Guid                 RequestId         { get; init; }
    public string               Stream            { get; init; }
    public SchemaDefinitionType DefaultSchemaType { get; init; }

    public StreamState    ExpectedStreamState    { get; init; }
    public StreamRevision ExpectedStreamRevision { get; init; }

    public bool HasStream => !IsNullOrWhiteSpace(Stream);
}

[PublicAPI]
public readonly record struct SendRequest() : ISendRequest {
    public static SendRequestBuilder Builder => new();

    public static readonly SendRequest Empty = new();

    public Guid                   RequestId         { get; init; } = Guid.NewGuid();
    public string                 Stream            { get; init; } = String.Empty;
    public IReadOnlyList<Message> Messages          { get; init; } = [];
    public SchemaDefinitionType   DefaultSchemaType { get; init; } = SchemaDefinitionType.Json;

    public StreamState    ExpectedStreamState    { get; init; } = StreamState.Any;
    public StreamRevision ExpectedStreamRevision { get; init; } = StreamRevision.Unset;
    
    public Headers Headers { get; init; } = new Headers();
    
    public bool HasStream => !IsNullOrWhiteSpace(Stream);

    public override string ToString() =>
        $"{nameof(RequestId)}: {RequestId}, {nameof(Stream)}: {Stream}, {nameof(Messages)}: {Messages.Count}";
}

[PublicAPI]
public record SendRequestBuilder {
    SendRequest Options { get; init; } = new();

    /// <summary>
    /// Sets the stream to send to.
    /// </summary>
    public SendRequestBuilder Stream(string stream) =>
        this with {
            Options = Options with {
                Stream = Ensure.NotNullOrWhiteSpace(stream)
            }
        };

    /// <summary>
    /// Adds many messages to the request. Ignores default schema type.
    /// </summary>
    public SendRequestBuilder Messages(params Message[] messages) {
        Ensure.NotNullOrEmpty(messages);
        Ensure.ItemsAreValid(messages, m => m.IsValid, m => m.RecordId);

        return this with {
            Options = Options with {
                Messages = Options.Messages.Concat(messages).ToList().AsReadOnly()
            }
        };
    }

    /// <summary>
    /// Adds a message to the request. Ignores default schema type.
    /// </summary>
    public SendRequestBuilder Message(Message message) =>
        Messages(message);

    public SendRequestBuilder Message(MessageBuilder message) =>
        Messages(message.WithSchemaType(Options.DefaultSchemaType).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message(Func<MessageBuilder, MessageBuilder> builder) =>
        Message(Producers.Message.Builder.WithSchemaType(Options.DefaultSchemaType).With(builder).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, PartitionKey key, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).Key(key).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, PartitionKey key, Headers headers, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).Key(key).Headers(headers).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, PartitionKey key, Headers headers, RecordId recordId, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).Key(key).Headers(headers).RecordId(recordId).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, Headers headers, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).Headers(headers).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, RecordId recordId, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).RecordId(recordId).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, Headers headers, RecordId recordId, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).Headers(headers).RecordId(recordId).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value) where T : class =>
        Message(value, Options.DefaultSchemaType);

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, PartitionKey key) where T : class =>
        Message(value, key, Options.DefaultSchemaType);

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, PartitionKey key, Headers headers) where T : class =>
        Message(value, key, headers, Options.DefaultSchemaType);

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, PartitionKey key, Headers headers, RecordId recordId) where T : class =>
        Message(value, key, headers, recordId, Options.DefaultSchemaType);

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, Headers headers) where T : class =>
        Message(value, headers, Options.DefaultSchemaType);

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, RecordId recordId) where T : class =>
        Message(value, recordId, Options.DefaultSchemaType);

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendRequestBuilder Message<T>(T value, Headers headers, RecordId recordId) where T : class =>
        Message(value, headers, recordId, Options.DefaultSchemaType);

    /// <summary>
    /// Sets the request id. 
    /// </summary>
    public SendRequestBuilder RequestId(Guid requestId) {
        return this with {
            Options = Options with {
                RequestId = Ensure.NotEmpty(requestId)
            }
        };
    }

    public SendRequestBuilder DefaultSchemaType(SchemaDefinitionType schemaType) {
        Ensure.NotDefault(schemaType, SchemaDefinitionType.Undefined);

        return this with {
            Options = Options with {
                DefaultSchemaType = schemaType
            }
        };
    }

    public SendRequestBuilder ExpectedStreamState(StreamState streamState) {
        return this with {
            Options = Options with {
                ExpectedStreamState = streamState
            }
        };
    }

    public SendRequestBuilder ExpectedStreamRevision(StreamRevision streamRevision) {
        return this with {
            Options = Options with {
                ExpectedStreamRevision = streamRevision
            }
        };
    }
    
    public SendRequestBuilder Headers(Headers headers) {
        Ensure.NotNull(headers);

        return this with {
            Options = Options with {
                Headers = headers
            }
        };
    }

    public SendRequestBuilder Headers(Action<Headers> setHeaders) {
        Ensure.NotNull(setHeaders);
        return Headers(Options.Headers.With(setHeaders));
    }

    /// <summary>
    /// Creates a new <see cref="SendRequest"/> from the current builder.
    /// </summary>
    public SendRequest Create() {

        var options = Options;
        
        // Set headers on all messages
        foreach (var (key, value) in Options.Headers)
        foreach (var message in options.Messages)
                message.Headers[key] = value;
        
        return options;
    }
}

[PublicAPI]
public readonly record struct SendSingleRequest() : ISendRequest {
    public static SendSingleRequestBuilder Builder => new();

    public static readonly SendSingleRequest Empty = new();

    public Guid                 RequestId         { get; init; } = Guid.NewGuid();
    public string               Stream            { get; init; } = String.Empty;
    public Message              Message           { get; init; } = Message.Empty;
    public SchemaDefinitionType DefaultSchemaType { get; init; } = SchemaDefinitionType.Json;

    public StreamState    ExpectedStreamState    { get; init; } = StreamState.Any;
    public StreamRevision ExpectedStreamRevision { get; init; } = StreamRevision.Unset;

    public bool HasStream => !IsNullOrWhiteSpace(Stream);

    public override string ToString() =>
        $"{nameof(RequestId)}: {RequestId}, {nameof(Stream)}: {Stream}, {nameof(RecordId)}: {Message.RecordId}, {nameof(Message)}: {Message.Value.GetType().Name}";
}

[PublicAPI]
public record SendSingleRequestBuilder {
    SendSingleRequest Options { get; init; } = new();

    /// <summary>
    /// Sets the stream to send to.
    /// </summary>
    public SendSingleRequestBuilder Stream(string stream) =>
        this with {
            Options = Options with {
                Stream = Ensure.NotNullOrWhiteSpace(stream)
            }
        };

    /// <summary>
    /// Adds a message to the request. Ignores default schema type.
    /// </summary>
    public SendSingleRequestBuilder Message(Message message) {
        Ensure.Valid(message, x => x.IsValid);

        return this with {
            Options = Options with {
                Message = message
            }
        };
    }

    public SendSingleRequestBuilder Message(MessageBuilder message) =>
        Message(message.WithSchemaType(Options.DefaultSchemaType).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message(Func<MessageBuilder, MessageBuilder> builder) =>
        Message(Producers.Message.Builder.WithSchemaType(Options.DefaultSchemaType).With(builder).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, PartitionKey key, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).Key(key).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, PartitionKey key, Headers headers, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).Key(key).Headers(headers).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, PartitionKey key, Headers headers, RecordId recordId, SchemaDefinitionType schemaType)
        where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).Key(key).Headers(headers).RecordId(recordId).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, Headers headers, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).Headers(headers).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, RecordId recordId, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).RecordId(recordId).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, Headers headers, RecordId recordId, SchemaDefinitionType schemaType) where T : class =>
        Message(Producers.Message.Builder.WithSchemaType(schemaType).Value(value).Headers(headers).RecordId(recordId).Create());

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value) where T : class =>
        Message(value, Options.DefaultSchemaType);

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, PartitionKey key) where T : class =>
        Message(value, key, Options.DefaultSchemaType);

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, PartitionKey key, Headers headers) where T : class =>
        Message(value, key, headers, Options.DefaultSchemaType);

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, PartitionKey key, Headers headers, RecordId recordId) where T : class =>
        Message(value, key, headers, recordId, Options.DefaultSchemaType);

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, Headers headers) where T : class =>
        Message(value, headers, Options.DefaultSchemaType);

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, RecordId recordId) where T : class =>
        Message(value, recordId, Options.DefaultSchemaType);

    /// <summary>
    /// Adds a message to the request.
    /// </summary>
    public SendSingleRequestBuilder Message<T>(T value, Headers headers, RecordId recordId) where T : class =>
        Message(value, headers, recordId, Options.DefaultSchemaType);

    /// <summary>
    /// Sets the request id. 
    /// </summary>
    public SendSingleRequestBuilder RequestId(Guid requestId) {
        return this with {
            Options = Options with {
                RequestId = Ensure.NotEmpty(requestId)
            }
        };
    }

    public SendSingleRequestBuilder DefaultSchemaType(SchemaDefinitionType schemaType) {
        Ensure.NotDefault(schemaType, SchemaDefinitionType.Undefined);

        return this with {
            Options = Options with {
                DefaultSchemaType = schemaType
            }
        };
    }

    public SendSingleRequestBuilder ExpectedStreamState(StreamState streamState) {
        return this with {
            Options = Options with {
                ExpectedStreamState = streamState
            }
        };
    }

    public SendSingleRequestBuilder ExpectedStreamRevision(StreamRevision streamRevision) {
        return this with {
            Options = Options with {
                ExpectedStreamRevision = streamRevision
            }
        };
    }

    /// <summary>
    /// Creates a new <see cref="SendRequest"/> from the current builder.
    /// </summary>
    public SendSingleRequest Create() {
        return Options with { };
    }
}