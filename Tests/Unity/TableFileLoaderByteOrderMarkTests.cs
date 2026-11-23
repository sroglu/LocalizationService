using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using PFound.Compression;

namespace PFound.LocalizationService.Unity.Tests
{
    /// <summary>
    /// <see cref="TableFileLoader"/> with tables that start with a UTF-8 byte order mark, as plain
    /// <c>.txt</c> files and inside compressed <c>.lzma</c> files: the first section of every table still
    /// loads. Tables without a mark load exactly as before.
    /// </summary>
    [TestFixture]
    public sealed class TableFileLoaderByteOrderMarkTests
    {
        private static readonly byte[] Utf8ByteOrderMark = { 0xEF, 0xBB, 0xBF };

        private static readonly LanguageKey En = new LanguageKey("en-US");
        private static readonly LanguageKey Tr = new LanguageKey("tr-TR");

        private const string ContentText =
            "[en-US]\n" +
            "hello=Hello\n" +
            "[tr-TR]\n" +
            "hello=Merhaba\n";

        private const string DefinitionsText =
            "[ParameterDefinitions]\n" +
            "count=int-n-NumberAbbreviated\n" +
            "[LanguageDefinitions]\n" +
            "en-US=English\n" +
            "tr-TR=Turkish\n" +
            "[End]\n";

        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pf_loc_loader_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [TestCase(LocalizationConstants.PlainExtension)]
        [TestCase(LocalizationConstants.CompressedExtension)]
        public void DefinitionsWithByteOrderMarkLoadTheirFirstSection(string extension)
        {
            WriteTable(LocalizationConstants.DefinitionsFilePrefix, extension, DefinitionsText, withByteOrderMark: true);

            var (definitions, _) = TableFileLoader.LoadFrom(_dir);

            Assert.That(definitions.ParametersFor("count").Count, Is.EqualTo(1), "first section [ParameterDefinitions] loads");
            Assert.That(definitions.Languages.Count, Is.EqualTo(2), "later sections still load");
        }

        [TestCase(LocalizationConstants.PlainExtension)]
        [TestCase(LocalizationConstants.CompressedExtension)]
        public void ContentWithByteOrderMarkLoadsItsFirstSection(string extension)
        {
            WriteTable(LocalizationConstants.ContentFilePrefix, extension, ContentText, withByteOrderMark: true);

            var (_, content) = TableFileLoader.LoadFrom(_dir);

            AssertContentLoads(content);
        }

        [TestCase(LocalizationConstants.PlainExtension)]
        [TestCase(LocalizationConstants.CompressedExtension)]
        public void TablesWithoutByteOrderMarkLoadAsBefore(string extension)
        {
            WriteTable(LocalizationConstants.DefinitionsFilePrefix, extension, DefinitionsText, withByteOrderMark: false);
            WriteTable(LocalizationConstants.ContentFilePrefix, extension, ContentText, withByteOrderMark: false);

            Assert.That(TableFileLoader.ReadTable(TablePath(LocalizationConstants.ContentFilePrefix, extension)), Is.EqualTo(ContentText),
                "the decoded text is unchanged");

            var (definitions, content) = TableFileLoader.LoadFrom(_dir);

            Assert.That(definitions.ParametersFor("count").Count, Is.EqualTo(1));
            Assert.That(definitions.Languages.Count, Is.EqualTo(2));
            AssertContentLoads(content);
        }

        // Loads, builds a catalog over the fallback (first) section, switches language and reads a string.
        private static void AssertContentLoads(InMemoryLocalizationSource content)
        {
            Assert.That(content.AvailableLanguages, Is.EqualTo(new[] { En, Tr }), "every language section loads, the first one included");

            var catalog = new LocalizationCatalog(content, En);
            Assert.That(catalog.Get("hello"), Is.EqualTo("Hello"));
            catalog.SwitchLanguage(Tr);
            Assert.That(catalog.Get("hello"), Is.EqualTo("Merhaba"));
        }

        private string TablePath(string prefix, string extension) => Path.Combine(_dir, prefix + extension);

        private void WriteTable(string prefix, string extension, string text, bool withByteOrderMark)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text); // GetBytes never emits the mark itself
            if (withByteOrderMark)
            {
                var marked = new byte[Utf8ByteOrderMark.Length + payload.Length];
                Buffer.BlockCopy(Utf8ByteOrderMark, 0, marked, 0, Utf8ByteOrderMark.Length);
                Buffer.BlockCopy(payload, 0, marked, Utf8ByteOrderMark.Length, payload.Length);
                payload = marked;
            }

            bool compressed = extension == LocalizationConstants.CompressedExtension;
            File.WriteAllBytes(TablePath(prefix, extension), compressed ? CompressionCodecs.Default.Compress(payload) : payload);

            if (compressed)
            {
                // The mark must survive the round trip, so the loader really meets it after decompression.
                byte[] inflated = CompressionCodecs.Default.Decompress(File.ReadAllBytes(TablePath(prefix, extension)));
                Assert.That(inflated, Is.EqualTo(payload), "precondition: the compressed table inflates to the marked bytes");
            }
        }
    }
}
