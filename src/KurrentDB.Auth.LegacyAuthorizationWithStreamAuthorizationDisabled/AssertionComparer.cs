// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace KurrentDB.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

internal sealed class AssertionComparer : IComparer<IAssertion> {
	private static readonly MethodInfo OpenTypeComparer =
		new Func<IAssertion, IAssertion, int>(Compare<object>).Method.GetGenericMethodDefinition();

	private AssertionComparer() { }

	public static IComparer<IAssertion> Instance { get; } = new AssertionComparer();

	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AllowAnonymousAssertion))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AndAssertion))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ClaimMatchAssertion))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ClaimValueMatchesParameterValueAssertion))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LegacyStreamPermissionAssertion))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MultipleClaimMatchAssertion))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(OrAssertion))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RequireAuthenticatedAssertion))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RequireStreamReadAssertion))]
	[UnconditionalSuppressMessage("Trimming", "IL2060",
		Justification = "All implementers of IAssertion are specified as DynamicDependency.")]
	public int Compare(IAssertion x, IAssertion y) {
		var grant = x.Grant.CompareTo(y.Grant);
		if (grant != 0)
			return grant * -1;

		var type = Comparer<Type>.Default.Compare(x.GetType(), y.GetType());
		if (type != 0)
			return type;

		var closed = (Func<IAssertion, IAssertion, int>)OpenTypeComparer.MakeGenericMethod(x.GetType())
			.CreateDelegate(typeof(Func<IAssertion, IAssertion, int>));
		return closed(x, y);
	}

	private static int Compare<T>(IAssertion x, IAssertion y) {
		if (x is IComparable<T> comparable)
			return comparable.CompareTo((T)y);
		throw new NotSupportedException(
			"Assertion classes must implement IComparable<T> where T is the Assertion class");
	}
}
