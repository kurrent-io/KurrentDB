// autogenerated
using System.Threading;

#pragma warning disable CS0108 // Member hides inherited member; missing new keyword
namespace KurrentDB.SourceGenerators.Tests.Messaging.FileScopedNamespace;
public partial class A
{
	public static string OriginalLabelStatic { get; } = "TestMessageGroup-FileScopedNamespace-A";
	public static string LabelStatic { get; set; } = "TestMessageGroup-FileScopedNamespace-A";
	public override string Label => LabelStatic;
}

public partial class B
{
	public static string OriginalLabelStatic { get; } = "TestMessageGroup-FileScopedNamespace-B";
	public static string LabelStatic { get; set; } = "TestMessageGroup-FileScopedNamespace-B";
	public override string Label => LabelStatic;
}
