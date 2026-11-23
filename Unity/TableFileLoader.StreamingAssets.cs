using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace PFound.LocalizationService.Unity
{
    /// <summary>
    /// Loads the table set from StreamingAssets on every platform. Where StreamingAssets is a folder (Editor,
    /// desktop, iOS) the files are read directly; where it is a URL (Android, where it lives inside the APK,
    /// and WebGL) they are fetched with <see cref="UnityWebRequest"/>. The tables are read straight into
    /// memory; nothing is copied to <see cref="PersistentTablesDir"/>.
    /// </summary>
    public static partial class TableFileLoader
    {
        /// <summary>
        /// Optional file in the tables folder that lists table file names, one per line, for table sets whose
        /// names carry a suffix (e.g. <c>LocalizationText-&lt;hash&gt;.lzma</c>). Required only where the
        /// folder cannot be listed (Android, WebGL); where present it is used on every platform, so all of
        /// them pick the same files. Exact names (<c>LocalizationText.txt</c>, ...) need no index.
        /// </summary>
        public const string TableIndexFileName = "index.txt";

        private static readonly string[] TableExtensions =
            { LocalizationConstants.PlainExtension, LocalizationConstants.CompressedExtension };

        /// <summary>
        /// Loads the definitions and content tables from <see cref="StreamingAssetsTablesDir"/> on any platform,
        /// with the same rules as <see cref="LoadFrom"/>: plain before compressed, exact names before suffixed
        /// ones. Suffixed names come from <see cref="TableIndexFileName"/> when it exists, otherwise from a
        /// folder listing (a URL cannot be listed). A missing table yields an empty part and logs a warning.
        /// Call from the main thread: the requests are created and awaited there.
        /// </summary>
        public static Task<(LocalizationDefinitions definitions, InMemoryLocalizationSource content)> LoadFromStreamingAssetsAsync()
            => LoadFromLocationAsync(StreamingAssetsTablesDir);

        // Test seam: a folder path, or a URL (jar:file://, file://, http(s)://) for the UnityWebRequest branch.
        internal static async Task<(LocalizationDefinitions definitions, InMemoryLocalizationSource content)> LoadFromLocationAsync(string location)
        {
            TableSource source = IsUrl(location) ? new UrlTableSource(location) : (TableSource)new FolderTableSource(location);
            if (!source.Exists)
            {
                Debug.LogWarning($"[Localization] Localizables directory not found: {location}");
                return BuildTables(null, null);
            }

            string defsText = await FindTableTextAsync(source, LocalizationConstants.DefinitionsFilePrefix);
            string contentText = await FindTableTextAsync(source, LocalizationConstants.ContentFilePrefix);
            return BuildTables(defsText, contentText);
        }

        // Android's StreamingAssets is a jar:file:// URL and WebGL's an http(s):// URL; elsewhere it is a folder.
        internal static bool IsUrl(string location) => location.IndexOf("://", StringComparison.Ordinal) >= 0;

        private static async Task<string> FindTableTextAsync(TableSource source, string prefix)
        {
            source.LastFailure = null;
            foreach (string extension in TableExtensions)
            {
                string exact = prefix + extension;
                byte[] bytes = await source.TryReadAsync(exact);
                if (bytes != null) return DecodeTable(exact, bytes);

                foreach (string name in await source.SuffixedCandidatesAsync(prefix, extension))
                {
                    if (string.Equals(name, exact, StringComparison.Ordinal)) continue;
                    bytes = await source.TryReadAsync(name);
                    if (bytes != null) return DecodeTable(name, bytes);
                }
            }

            string failure = source.LastFailure != null ? " Last request failed: " + source.LastFailure : "";
            Debug.LogWarning($"[Localization] No '{prefix}' table found in {source.Location}.{failure}");
            return null;
        }

        // Names listed in the index file: UTF-8, one per line; blank lines, '#' comments and paths are skipped.
        private static List<string> ParseIndex(byte[] bytes)
        {
            var names = new List<string>();
            string text = Encoding.UTF8.GetString(bytes).TrimStart((char)0xFEFF);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.IndexOf('/') >= 0 || line.IndexOf('\\') >= 0) continue;
                names.Add(line);
            }
            return names;
        }

        /// <summary>Where the table bytes come from: a folder (file reads) or a URL (UnityWebRequest).</summary>
        private abstract class TableSource
        {
            public readonly string Location;
            private bool _indexRead;
            private List<string> _index; // null when there is no index file

            protected TableSource(string location) { Location = location; }

            public virtual bool Exists => true;

            /// <summary>The last failed request (file, error, response code); null where there are no requests.</summary>
            public string LastFailure { get; set; }

            /// <summary>The file's bytes, or null when it is not there.</summary>
            public abstract Task<byte[]> TryReadAsync(string fileName);

            /// <summary>Suffixed names found by listing the folder; empty where listing is impossible.</summary>
            protected abstract IEnumerable<string> ListFolder(string prefix, string extension);

            /// <summary>
            /// Suffixed table names for <paramref name="prefix"/> and <paramref name="extension"/>: from the index
            /// file when there is one (so every platform picks the same file), otherwise from a folder listing.
            /// </summary>
            public async Task<IEnumerable<string>> SuffixedCandidatesAsync(string prefix, string extension)
            {
                if (!_indexRead)
                {
                    byte[] bytes = await TryReadAsync(TableIndexFileName);
                    _index = bytes != null ? ParseIndex(bytes) : null;
                    _indexRead = true;
                }
                if (_index == null) return ListFolder(prefix, extension);

                var matches = new List<string>();
                foreach (string name in _index)
                    if (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                        matches.Add(name);
                return matches;
            }
        }

        private sealed class FolderTableSource : TableSource
        {
            public FolderTableSource(string directory) : base(directory) { }

            public override bool Exists => Directory.Exists(Location);

            public override Task<byte[]> TryReadAsync(string fileName)
            {
                string path = Path.Combine(Location, fileName);
                return Task.FromResult(File.Exists(path) ? File.ReadAllBytes(path) : null);
            }

            protected override IEnumerable<string> ListFolder(string prefix, string extension)
            {
                var names = new List<string>();
                foreach (string path in Directory.GetFiles(Location, prefix + "*" + extension))
                    names.Add(Path.GetFileName(path));
                return names;
            }
        }

        private sealed class UrlTableSource : TableSource
        {
            public UrlTableSource(string url) : base(url.TrimEnd('/')) { }

            public override Task<byte[]> TryReadAsync(string fileName)
            {
                var completion = new TaskCompletionSource<byte[]>();
                var request = UnityWebRequest.Get(Location + "/" + Uri.EscapeDataString(fileName));
                // Fires on the main thread, also when the request has already finished.
                request.SendWebRequest().completed += _ =>
                {
                    try
                    {
                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            completion.TrySetResult(request.downloadHandler.data);
                        }
                        else
                        {
                            LastFailure = DescribeFailure(fileName, request);
                            completion.TrySetResult(null);
                        }
                    }
                    catch (Exception e)
                    {
                        completion.TrySetException(e);
                    }
                    finally
                    {
                        request.Dispose();
                    }
                };
                return completion.Task;
            }

            // A URL cannot be listed: suffixed names need the index file.
            protected override IEnumerable<string> ListFolder(string prefix, string extension) => Array.Empty<string>();

            // "<file>: <error> (response code <n>)"; the code only when the request got a response.
            private static string DescribeFailure(string fileName, UnityWebRequest request)
                => fileName + ": " + (request.error ?? request.result.ToString())
                 + (request.responseCode > 0 ? " (response code " + request.responseCode + ")" : "");
        }
    }
}
