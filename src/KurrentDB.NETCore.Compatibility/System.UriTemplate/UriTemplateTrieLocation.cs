#pragma warning disable IDE0073 // The file header does not match the required text
//----------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//----------------------------------------------------------------

namespace System;

class UriTemplateTrieLocation {
	public UriTemplateTrieIntraNodeLocation locationWithin;
	public UriTemplateTrieNode node;
	public UriTemplateTrieLocation(UriTemplateTrieNode n, UriTemplateTrieIntraNodeLocation i) {
		this.node = n;
		this.locationWithin = i;
	}
}
