namespace KurrentDB.SourceGenerators.Tests.Messaging.NestedDerived {
	[DerivedMessage(TestMessageGroup.NestedDerived)]
	partial class B : Message {
		[DerivedMessage(TestMessageGroup.NestedDerived)]
		private partial class A : B {
		}
	}
}
