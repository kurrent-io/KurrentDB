namespace KurrentDB.SourceGenerators.Tests.Messaging.Nested {
	public partial class N {
		[DerivedMessage(TestMessageGroup.Nested)]
		public partial class A : Message {
		}

		[DerivedMessage(TestMessageGroup.Nested)]
		public partial class B : Message {
		}
	}
}
