using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PFound.Compression;
using UnityEngine;
using UnityEngine.TestTools;

namespace PFound.LocalizationService.Unity.Tests
{
    /// <summary>
    /// <see cref="TableFileLoader.LoadFromStreamingAssetsAsync"/> through its internal location seam. Each
    /// case runs on both branches: a folder path (file reads, as in the Editor, desktop and iOS) and a
    /// <c>file://</c> URL that forces the <c>UnityWebRequest</c> branch Android and WebGL use.
    /// </summary>
    [TestFixture]
    public sealed class StreamingAssetsLoadingTests
    {
        private enum Branch { File, WebRequest }

        private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(20);
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

        private const string Definitions = LocalizationConstants.DefinitionsFilePrefix;
        private const string Content = LocalizationConstants.ContentFilePrefix;

        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pf_loc_sa_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        // --- exact names: no extra file needed ---------------------------------------------------------

        [UnityTest] public IEnumerator ExactPlainTablesWithByteOrderMark_File() => ExactPlainTablesWithByteOrderMark(Branch.File);
        [UnityTest] public IEnumerator ExactPlainTablesWithByteOrderMark_WebRequest() => ExactPlainTablesWithByteOrderMark(Branch.WebRequest);

        private IEnumerator ExactPlainTablesWithByteOrderMark(Branch branch)
        {
            WriteTable(Definitions + ".txt", DefinitionsText);
            WriteTable(Content + ".txt", ContentText);
            return Load(Location(branch), AssertTablesLoad);
        }

        [UnityTest] public IEnumerator ExactCompressedTablesWithByteOrderMark_File() => ExactCompressedTablesWithByteOrderMark(Branch.File);
        [UnityTest] public IEnumerator ExactCompressedTablesWithByteOrderMark_WebRequest() => ExactCompressedTablesWithByteOrderMark(Branch.WebRequest);

        private IEnumerator ExactCompressedTablesWithByteOrderMark(Branch branch)
        {
            WriteTable(Definitions + ".lzma", DefinitionsText);
            WriteTable(Content + ".lzma", ContentText);
            return Load(Location(branch), AssertTablesLoad);
        }

        [UnityTest] public IEnumerator PlainTablesArePreferredOverCompressed_File() => PlainTablesArePreferredOverCompressed(Branch.File);
        [UnityTest] public IEnumerator PlainTablesArePreferredOverCompressed_WebRequest() => PlainTablesArePreferredOverCompressed(Branch.WebRequest);

        private IEnumerator PlainTablesArePreferredOverCompressed(Branch branch)
        {
            WriteTable(Content + ".txt", ContentText);
            WriteTable(Content + ".lzma", "[en-US]\nhello=From the compressed table\n");
            return Load(Location(branch), tables =>
                Assert.That(tables.content.Load(En)["hello"], Is.EqualTo("Hello"), "the plain table wins, as in LoadFrom"));
        }

        // --- suffixed names ----------------------------------------------------------------------------

        [UnityTest] public IEnumerator SuffixedTablesListedInTheIndex_File() => SuffixedTablesListedInTheIndex(Branch.File);
        [UnityTest] public IEnumerator SuffixedTablesListedInTheIndex_WebRequest() => SuffixedTablesListedInTheIndex(Branch.WebRequest);

        private IEnumerator SuffixedTablesListedInTheIndex(Branch branch)
        {
            WriteSuffixedTablesAndIndex();
            return Load(Location(branch), AssertTablesLoad);
        }

        [UnityTest]
        public IEnumerator SuffixedTablesWithoutIndexAreFoundByListingTheFolder()
        {
            WriteTable(Definitions + "-1a2b.txt", DefinitionsText);
            WriteTable(Content + "-1a2b.lzma", ContentText);
            return Load(Location(Branch.File), AssertTablesLoad);
        }

        [UnityTest]
        public IEnumerator SuffixedTablesWithoutIndexAreNotFoundOverAUrl()
        {
            WriteTable(Definitions + "-1a2b.txt", DefinitionsText);
            WriteTable(Content + "-1a2b.lzma", ContentText);
            LogAssert.Expect(LogType.Warning, new Regex("No '" + Definitions + "' table found"));
            LogAssert.Expect(LogType.Warning, new Regex("No '" + Content + "' table found"));
            return Load(Location(Branch.WebRequest), AssertEmpty);
        }

        // --- missing tables: empty result and a warning, no exception -----------------------------------

        [UnityTest] public IEnumerator MissingTablesGiveAnEmptyResultAndWarnings_File() => MissingTablesGiveAnEmptyResultAndWarnings(Branch.File);
        [UnityTest] public IEnumerator MissingTablesGiveAnEmptyResultAndWarnings_WebRequest() => MissingTablesGiveAnEmptyResultAndWarnings(Branch.WebRequest);

        private IEnumerator MissingTablesGiveAnEmptyResultAndWarnings(Branch branch)
        {
            LogAssert.Expect(LogType.Warning, new Regex("No '" + Definitions + "' table found"));
            LogAssert.Expect(LogType.Warning, new Regex("No '" + Content + "' table found"));
            return Load(Location(branch), AssertEmpty);
        }

        [UnityTest]
        public IEnumerator MissingFolderGivesAnEmptyResultAndAWarning_File()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Localizables directory not found"));
            return Load(Path.Combine(_dir, "missing"), AssertEmpty);
        }

        [UnityTest]
        public IEnumerator MissingFolderGivesAnEmptyResultAndWarnings_WebRequest()
        {
            // A URL cannot be probed for a folder, so each table reports itself missing.
            LogAssert.Expect(LogType.Warning, new Regex("No '" + Definitions + "' table found"));
            LogAssert.Expect(LogType.Warning, new Regex("No '" + Content + "' table found"));
            return Load(new Uri(Path.Combine(_dir, "missing")).AbsoluteUri, AssertEmpty);
        }

        // --- threading and the public entry point ----------------------------------------------------------

        [UnityTest]
        public IEnumerator WebRequestBranchStaysOnTheMainThread()
        {
            // Several requests in a row (index, a stale entry, the listed tables): each one is created in the
            // continuation of the previous await, so finishing on the calling thread means none moved off it.
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            WriteSuffixedTablesAndIndex();

            var task = TableFileLoader.LoadFromLocationAsync(Location(Branch.WebRequest));
            int finishedOn = -1;
            task.ContinueWith(_ => finishedOn = Thread.CurrentThread.ManagedThreadId, TaskContinuationOptions.ExecuteSynchronously);

            return Await(task, tables =>
            {
                AssertTablesLoad(tables);
                Assert.That(finishedOn, Is.EqualTo(mainThread), "the load finished on the thread that started it");
            });
        }

        [UnityTest]
        public IEnumerator LoadFromStreamingAssetsReadsTheEditorFolder()
        {
            // In the Editor StreamingAssets is a folder, so this is the file branch. ProjectFoundation ships no
            // tables there; if a project does, they simply load.
            bool present = Directory.Exists(TableFileLoader.StreamingAssetsTablesDir);
            if (!present) LogAssert.Expect(LogType.Warning, new Regex("Localizables directory not found"));

            return Await(TableFileLoader.LoadFromStreamingAssetsAsync(), tables =>
            {
                Assert.That(tables.definitions, Is.Not.Null);
                Assert.That(tables.content, Is.Not.Null);
                if (!present) AssertEmpty(tables);
            });
        }

        [TestCase("jar:file:///data/app/~~a==/com.example.game-b==/base.apk!/assets/Localizables", true)]
        [TestCase("https://example.com/game/StreamingAssets/Localizables", true)]
        [TestCase("file:///private/tmp/Localizables", true)]
        [TestCase("/Users/me/Game/Assets/StreamingAssets/Localizables", false)]
        [TestCase(@"C:\Game\Assets\StreamingAssets\Localizables", false)]
        [TestCase("/var/containers/Bundle/Application/X/Game.app/Data/Raw/Localizables", false)]
        public void UrlLocationsUseTheWebRequestBranch(string location, bool isUrl)
        {
            Assert.That(TableFileLoader.IsUrl(location), Is.EqualTo(isUrl));
        }

        // --- helpers ---------------------------------------------------------------------------------------

        private string Location(Branch branch) => branch == Branch.File ? _dir : new Uri(_dir).AbsoluteUri;

        private void WriteSuffixedTablesAndIndex()
        {
            WriteTable(Definitions + "-1a2b.txt", DefinitionsText);
            WriteTable(Content + "-1a2b.lzma", ContentText);
            // BOM, CRLF, a comment, a blank line, a path (ignored) and a stale entry listed first (skipped).
            string index =
                "# table set 1a2b\r\n" +
                "\r\n" +
                "sub/" + Content + "-1a2b.txt\r\n" +
                Content + "-0000.lzma\r\n" +
                Definitions + "-1a2b.txt\r\n" +
                Content + "-1a2b.lzma\r\n";
            File.WriteAllBytes(Path.Combine(_dir, TableFileLoader.TableIndexFileName), WithByteOrderMark(Encoding.UTF8.GetBytes(index)));
        }

        private void WriteTable(string fileName, string text)
        {
            byte[] bytes = WithByteOrderMark(Encoding.UTF8.GetBytes(text)); // GetBytes never emits the mark itself
            if (fileName.EndsWith(LocalizationConstants.CompressedExtension, StringComparison.Ordinal))
                bytes = CompressionCodecs.Default.Compress(bytes);
            File.WriteAllBytes(Path.Combine(_dir, fileName), bytes);
        }

        private static byte[] WithByteOrderMark(byte[] payload)
        {
            var bytes = new byte[Utf8ByteOrderMark.Length + payload.Length];
            Buffer.BlockCopy(Utf8ByteOrderMark, 0, bytes, 0, Utf8ByteOrderMark.Length);
            Buffer.BlockCopy(payload, 0, bytes, Utf8ByteOrderMark.Length, payload.Length);
            return bytes;
        }

        private static IEnumerator Load(string location, Action<(LocalizationDefinitions definitions, InMemoryLocalizationSource content)> then)
            => Await(TableFileLoader.LoadFromLocationAsync(location), then);

        // Polls instead of blocking: the requests only progress while the Editor keeps updating.
        private static IEnumerator Await<T>(Task<T> task, Action<T> then)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (watch.Elapsed > LoadTimeout) Assert.Fail("The table load did not finish within " + LoadTimeout.TotalSeconds + " s.");
                yield return null;
            }
            then(task.GetAwaiter().GetResult());
        }

        // Every section of both tables loads, and a catalog over them switches language and reads a string.
        private static void AssertTablesLoad((LocalizationDefinitions definitions, InMemoryLocalizationSource content) tables)
        {
            Assert.That(tables.definitions.ParametersFor("count").Count, Is.EqualTo(1), "definitions: first section loads");
            Assert.That(tables.definitions.Languages.Count, Is.EqualTo(2), "definitions: later sections load");
            Assert.That(tables.content.AvailableLanguages, Is.EqualTo(new[] { En, Tr }), "content: every language section loads");

            var catalog = new LocalizationCatalog(tables.content, En, tables.definitions);
            Assert.That(catalog.Get("hello"), Is.EqualTo("Hello"));
            catalog.SwitchLanguage(Tr);
            Assert.That(catalog.Get("hello"), Is.EqualTo("Merhaba"));
        }

        private static void AssertEmpty((LocalizationDefinitions definitions, InMemoryLocalizationSource content) tables)
        {
            Assert.That(tables.definitions.Parameters.Count, Is.EqualTo(0), "no definitions");
            Assert.That(tables.definitions.Languages.Count, Is.EqualTo(0), "no language definitions");
            Assert.That(tables.content.AvailableLanguages.Count, Is.EqualTo(0), "no content");
        }
    }
}
