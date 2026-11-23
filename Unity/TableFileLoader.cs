using System.IO;
using System.Text;
using UnityEngine;
using PFound.Compression;

namespace PFound.LocalizationService.Unity
{
    /// <summary>
    /// Loads localization table text from the engine's file locations. Plain <c>.txt</c> tables are read
    /// directly; compressed <c>.lzma</c> tables are inflated with the shared PFound.Compression codec.
    /// Resolves the <c>Localizables</c> folder under either
    /// <see cref="Application.streamingAssetsPath"/> or <see cref="Application.persistentDataPath"/>.
    /// </summary>
    public static partial class TableFileLoader
    {
        public static string StreamingAssetsTablesDir
            => Path.Combine(Application.streamingAssetsPath, LocalizationConstants.TablesFolderName);

        public static string PersistentTablesDir
            => Path.Combine(Application.persistentDataPath, LocalizationConstants.TablesFolderName);

        /// <summary>Reads a table file, decompressing if it carries the compressed extension.</summary>
        public static string ReadTable(string path) => DecodeTable(path, File.ReadAllBytes(path));

        // Inflates the bytes when the name carries the compressed extension, then decodes UTF-8.
        private static string DecodeTable(string name, byte[] bytes)
        {
            if (name.EndsWith(LocalizationConstants.CompressedExtension, System.StringComparison.OrdinalIgnoreCase))
                bytes = CompressionCodecs.Default.Decompress(bytes);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Builds a directory-backed table set from a folder, transparently handling either the plain or
        /// the compressed variant of each file.
        /// </summary>
        public static (LocalizationDefinitions definitions, InMemoryLocalizationSource content) LoadFrom(string directory)
        {
            string defsText = ReadPrefixed(directory, LocalizationConstants.DefinitionsFilePrefix);
            string contentText = ReadPrefixed(directory, LocalizationConstants.ContentFilePrefix);
            return BuildTables(defsText, contentText);
        }

        // A missing table (null text) yields an empty part, as it always has.
        private static (LocalizationDefinitions definitions, InMemoryLocalizationSource content) BuildTables(string defsText, string contentText)
        {
            var definitions = defsText != null
                ? LocalizationDefinitions.FromIni(IniDocument.Parse(defsText))
                : new LocalizationDefinitions();
            var content = contentText != null
                ? ContentTables.FromText(contentText)
                : new InMemoryLocalizationSource();

            return (definitions, content);
        }

        private static string ReadPrefixed(string directory, string prefix)
        {
            // Prefer plain over compressed; accept an optional "-suffix" variant.
            foreach (var ext in new[] { LocalizationConstants.PlainExtension, LocalizationConstants.CompressedExtension })
            {
                string exact = Path.Combine(directory, prefix + ext);
                if (File.Exists(exact)) return ReadTable(exact);
                if (Directory.Exists(directory))
                    foreach (var path in Directory.GetFiles(directory, prefix + "*" + ext))
                        return ReadTable(path);
            }
            return null;
        }
    }
}
