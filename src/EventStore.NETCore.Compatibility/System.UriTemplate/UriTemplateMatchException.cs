//----------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//----------------------------------------------------------------

namespace System {
	using System.Runtime.CompilerServices;

	[TypeForwardedFrom("System.ServiceModel.Web, Version=3.5.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")]
	[Serializable]
	public class UriTemplateMatchException : SystemException {
		public UriTemplateMatchException(string message)
			: base(message) {
		}
	}
}
