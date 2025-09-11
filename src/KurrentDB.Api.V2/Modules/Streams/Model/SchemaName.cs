namespace KurrentDB.Api.Streams;

public readonly record struct SchemaName(string Value) {
    public static readonly SchemaName None = new("");

    public string Value { get; } = Value;

    public override string ToString() => Value;

    public static SchemaName From(string value) {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"SchemaName '{value}' is not valid.", nameof(value))
            : new(value);
    }

    public static implicit operator SchemaName(string _)  => From(_);
    public static implicit operator string(SchemaName id) => id.ToString();
}
