// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Globalization;
using System.Text;
using EventStore.Common.Utils;

namespace EventStore.Transport.Http;

public class MediaType {
	public readonly string Range;
	public readonly string Type;
	public readonly string Subtype;
	public readonly float Priority;
	public readonly bool EncodingSpecified;
	public readonly Encoding Encoding;

	public MediaType(string range, string type, string subtype, float priority, bool encodingSpecified,
		Encoding encoding) {
		Range = range;
		Type = type;
		Subtype = subtype;
		Priority = priority;
		EncodingSpecified = encodingSpecified;
		Encoding = encoding;
	}

	public MediaType(string range, string type, string subtype, float priority)
		: this(range, type, subtype, priority, false, null) {
	}

	public bool Matches(string mediaRange, Encoding encoding) {
		if (EncodingSpecified)
			return Range == mediaRange && Encoding.Equals(encoding);

		return Range == mediaRange;
	}

	public static bool TryParse(string componentText, out MediaType result) {
		return TryParseInternal(componentText, false, out result);
	}

	public static MediaType TryParse(string componentText) {
		MediaType result;
		return TryParseInternal(componentText, false, out result) ? result : null;
	}

	public static MediaType Parse(string componentText) {
		MediaType result;
		if (!TryParseInternal(componentText, true, out result))
			throw new Exception("This should never happen!");
		return result;
	}

	private static bool TryParseInternal(string componentText, bool throwExceptions, out MediaType result) {
		result = null;

		if (componentText == null) {
			if (throwExceptions)
				throw new ArgumentNullException("componentText");
			return false;
		}

		if (componentText == "") {
			if (throwExceptions)
				throw new ArgumentException("componentText");
			return false;
		}

		var priority = 1.0f; // default priority
		var encodingSpecified = false;
		Encoding encoding = null;

		var parts = componentText.Split(';');
		var mediaRange = parts[0];

		var typeParts = mediaRange.Split(new[] {'/'}, 2);
		if (typeParts.Length != 2) {
			if (throwExceptions)
				throw new ArgumentException("componentText");
			return false;
		}

		var mediaType = typeParts[0];
		var mediaSubtype = typeParts[1];

		if (parts.Length > 1) {
			for (var i = 1; i < parts.Length; i++) {
				var part = parts[i].ToLowerInvariant().Trim();
				if (part.StartsWith("q=")) {
					var quality = part.Substring(2);
					float q;
					if (float.TryParse(quality, NumberStyles.Float, CultureInfo.InvariantCulture, out q)) {
						priority = q;
					}
				} else if (part.StartsWith("charset=")) {
					encodingSpecified = true;
					try {
						var encName = part.Substring(8);
						encoding = encName.Equals("utf-8", StringComparison.OrdinalIgnoreCase)
							? Helper.UTF8NoBom
							: Encoding.GetEncoding(encName);
					} catch (ArgumentException) {
						encoding = null;
					}
				}
			}
		}

		result = new MediaType(mediaRange, mediaType, mediaSubtype, priority, encodingSpecified, encoding);
		return true;
	}
}
