#pragma warning disable IDE0073 // The file header does not match the required text
//The Inflector extensions were partially cloned from Inflector (https://github.com/srkirkland/Inflector)

//The MIT License (MIT)

//Copyright (c) 2013 Scott Kirkland

//Permission is hereby granted, free of charge, to any person obtaining a copy of
//this software and associated documentation files (the "Software"), to deal in
//the Software without restriction, including without limitation the rights to
//use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
//the Software, and to permit persons to whom the Software is furnished to do so,
//subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
//FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
//COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
//IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
//CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System.Text.RegularExpressions;

namespace EventStore.Plugins;

static class InflectorExtensions {
	/// <summary>
	///     Separates the input words with underscore
	/// </summary>
	public static string Underscore(this string input) =>
		Regex.Replace(Regex.Replace(Regex.Replace(input, @"([\p{Lu}]+)([\p{Lu}][\p{Ll}])", "$1_$2"), @"([\p{Ll}\d])([\p{Lu}])", "$1_$2"), @"[-\s]", "_")
			.ToLowerInvariant();

	/// <summary>
	///     Replaces underscores with dashes in the string
	/// </summary>
	public static string Dasherize(this string underscoredWord) => underscoredWord.Replace('_', '-');

	/// <summary>
	///     Separates the input words with hyphens and all the words are converted to lowercase
	/// </summary>
	public static string Kebaberize(this string input) => Underscore(input).Dasherize();
}
