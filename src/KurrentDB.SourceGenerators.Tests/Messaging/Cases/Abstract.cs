namespace KurrentDB.SourceGenerators.Tests.Messaging.Abstract {
	[DerivedMessage]
	abstract partial class B : Message {
		[DerivedMessage(TestMessageGroup.Abstract)]
		private partial class A : B {
		}
	}
}
