// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.Http;

namespace Kurrent.Kontext.Testing;

/// <summary>
/// Fetches the files of a model that only a test or a benchmark needs, the first time that code
/// actually runs.
/// </summary>
/// <remarks>
/// <para>Deliberately not an MSBuild target: a target fires on build, and CI builds every project in
/// the solution, so a comparison model nobody runs would still be paid for on every clone. Called
/// from a test hook or a benchmark leg instead, the bytes arrive only when that leg does. Models
/// Kontext needs to RUN stay in KurrentDB.Kontext.Models, downloaded at build time.</para>
/// <para>Synchronous on purpose. The callers are class-setup hooks and constructor initializers,
/// which cannot await, and a one-off fetch at startup gains nothing from going async.</para>
/// </remarks>
public static class ModelCache {
    /// <summary>
    /// Ensures every file exists under <paramref name="directory"/>, downloading the missing ones,
    /// and returns that directory.
    /// </summary>
    public static string Ensure(string directory, IReadOnlyList<(string Url, string RelativePath)> files) {
        foreach (var (url, relativePath) in files) {
            var path = Path.Combine(directory, relativePath);

            if (!File.Exists(path))
                Download(url, path);
        }

        return directory;
    }

    static void Download(string url, string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        Console.WriteLine($"[models] downloading {url}");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        // Write to a unique temporary name and move it into place. An interrupted transfer would
        // otherwise leave a truncated file that the File.Exists check above accepts forever, and two
        // processes downloading the same model at once would write over each other mid-stream.
        var partial = $"{path}.{Guid.NewGuid():N}.partial";

        try {
            using var request  = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            using (var source = response.Content.ReadAsStream())
            using (var target = File.Create(partial))
                source.CopyTo(target);

            File.Move(partial, path, overwrite: true);
        } finally {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }
}
