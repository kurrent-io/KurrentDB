using System;

namespace KurrentDB.SourceGenerators.Tests.Messaging;

[AttributeUsage(AttributeTargets.Class)]
public class BaseMessageAttribute : Attribute {
	public BaseMessageAttribute() {
	}
}

[AttributeUsage(AttributeTargets.Class)]
public class DerivedMessageAttribute : Attribute {
	public DerivedMessageAttribute() {
	}

	public DerivedMessageAttribute(object messageGroup) {
	}
}

[BaseMessage]
public abstract partial class Message {
}

enum TestMessageGroup {
	None,
	Abstract,
	AbstractWithGroup,
	FileScopedNamespace,
	Impartial,
	ImpartialNested,
	Nested,
	NestedDerived,
	Simple,
}