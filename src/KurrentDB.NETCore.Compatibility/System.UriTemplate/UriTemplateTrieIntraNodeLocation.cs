#pragma warning disable IDE0073 // The file header does not match the required text
//----------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//----------------------------------------------------------------

namespace System;

enum UriTemplateTrieIntraNodeLocation {
	BeforeLiteral,
	AfterLiteral,
	AfterCompound,
	AfterVariable,
}
