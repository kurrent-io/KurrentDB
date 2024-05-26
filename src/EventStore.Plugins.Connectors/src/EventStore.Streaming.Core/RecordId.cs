namespace EventStore.Streaming;

public readonly record struct RecordId {
	public static readonly RecordId None = new(Guid.Empty.ToString());
	
	RecordId(string value) => Value = value;

	internal string Value { get; }

	public override string ToString() => Value;

	public static RecordId From(string value) {
		if (!Guid.TryParse(value, out _) || value == Guid.Empty.ToString())
			throw new InvalidRecordId(value);
		
		return new(value);
	}

	public static RecordId From(Guid value) => 
		From(value.ToString());

	public static implicit operator Guid(RecordId _) => Guid.Parse(_.Value);
	public static implicit operator RecordId(Guid _) => From(_);
}

public class InvalidRecordId(string value) : ArgumentException($"Record id is invalid: {value}");