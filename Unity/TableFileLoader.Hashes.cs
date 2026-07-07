using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PFound.LocalizationService.Unity
{
    /// <summary>
    /// Hash-name resolution for the on-disk localization table set. Deployed tables may be named with a
    /// trailing content-address hash (e.g. <c>LocalizationText-&lt;hash&gt;.lzma</c>) so the runtime can tell
    /// deterministically which table set is present and compare it against a remote manifest — instead of
    /// blindly taking the first file that matches the prefix.
    /// </summary>
    public static partial class TableFileLoader
    {
        /// <summary>
        /// Extracts the hash segment from a table file name: strips the extension, then returns the text
        /// after the last <see cref="LocalizationConstants.FileNameDelimiter"/>. A file with no delimiter
        /// (an un-hashed baseline such as <c>LocalizationText.txt</c>) yields its bare name.
        /// </summary>
        public static string GetFileHashFromFileName(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            int idx = name.LastIndexOf(LocalizationConstants.FileNameDelimiter);
            return idx >= 0 ? name.Substring(idx + 1) : name;
        }

        /// <summary>
        /// Resolves the content + definitions hashes for the deployed table set under the StreamingAssets
        /// Localizables folder. See <see cref="GetLocalFileHashes(string)"/>.
        /// </summary>
        public static LocalizationHashData GetLocalFileHashes()
            => GetLocalFileHashes(StreamingAssetsTablesDir);

        /// <summary>
        /// Scans <paramref name="directory"/> for exactly one definitions file and exactly one content
        /// file (matched by prefix; the plain and compressed variants of the same file share a hash and
        /// count once) and returns their hashes as a <see cref="LocalizationHashData"/>. A missing
        /// directory, a missing file, or an ambiguous match is a boundary condition: it logs a warning and
        /// returns <see cref="LocalizationHashData.Invalid"/>.
        /// </summary>
        public static LocalizationHashData GetLocalFileHashes(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    Debug.LogWarning($"[Localization] Localizables directory not found: {directory}");
                    return LocalizationHashData.Invalid;
                }

                string definitionsHash = ResolveSingleHash(directory, LocalizationConstants.DefinitionsFilePrefix);
                string contentHash = ResolveSingleHash(directory, LocalizationConstants.ContentFilePrefix);
                if (definitionsHash == null || contentHash == null)
                    return LocalizationHashData.Invalid;

                return new LocalizationHashData(contentHash, definitionsHash);
            }
            catch (IOException ex)
            {
                Debug.LogWarning($"[Localization] Failed to read localizables directory '{directory}': {ex.Message}");
                return LocalizationHashData.Invalid;
            }
        }

        /// <summary>
        /// Finds the single hash carried by the files that start with <paramref name="prefix"/> in
        /// <paramref name="directory"/>. Both table extensions are considered; because the plain and
        /// compressed variants of the same table carry the same hash, distinct hashes must number exactly
        /// one. Returns null (with a warning) when none or more than one is present.
        /// </summary>
        private static string ResolveSingleHash(string directory, string prefix)
        {
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ext in new[] { LocalizationConstants.PlainExtension, LocalizationConstants.CompressedExtension })
                foreach (var path in Directory.GetFiles(directory, prefix + "*" + ext))
                    hashes.Add(GetFileHashFromFileName(path));

            if (hashes.Count == 0)
            {
                Debug.LogWarning($"[Localization] No '{prefix}' table file found in {directory}.");
                return null;
            }
            if (hashes.Count > 1)
            {
                Debug.LogWarning($"[Localization] Expected exactly one '{prefix}' table set in {directory}, found {hashes.Count} distinct hashes.");
                return null;
            }

            foreach (var hash in hashes) return hash;
            return null;
        }
    }
}
