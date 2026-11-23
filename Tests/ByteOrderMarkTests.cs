using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace PFound.LocalizationService.Tests
{
    /// <summary>
    /// A UTF-8 byte order mark (bytes EF BB BF; U+FEFF once decoded) at the start of a table must not hide
    /// the table's first section, and text without one must parse exactly as before. Engine-free paths
    /// only; the Unity file loader (plain and compressed tables) has its own tests in the Unity test assembly.
    /// </summary>
    [TestFixture]
    public sealed class ByteOrderMarkTests
    {
        private const string Bom = "\uFEFF";

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
            "[DynamicKeyDefinitions]\n" +
            "item_{id}_name=int-id\n" +
            "[LanguageDefinitions]\n" +
            "en-US=English\n" +
            "tr-TR=Turkish\n" +
            "[End]\n";

        [Test]
        public void ContentWithByteOrderMarkLoadsItsFirstSection()
        {
            var content = ContentTables.FromText(Bom + ContentText);

            Assert.That(content.AvailableLanguages, Is.EqualTo(new[] { En, Tr }), "every language section loads, the first one included");
            Assert.That(content.Load(En)["hello"], Is.EqualTo("Hello"), "first section entries load");

            // The fallback language is the first section; without it the catalog cannot be built.
            var catalog = new LocalizationCatalog(content, En);
            Assert.That(catalog.Get("hello"), Is.EqualTo("Hello"));
        }

        [Test]
        public void DefinitionsWithByteOrderMarkLoadTheirFirstSection()
        {
            var definitions = LocalizationDefinitions.FromIni(IniDocument.Parse(Bom + DefinitionsText));

            Assert.That(definitions.ParametersFor("count").Count, Is.EqualTo(1), "first section [ParameterDefinitions] loads");
            Assert.That(definitions.TryGetDynamicKey("item_{id}_name", out _), Is.True, "later sections still load");
            Assert.That(definitions.Languages.Count, Is.EqualTo(2), "later sections still load");
        }

        [Test]
        public void TextWithoutByteOrderMarkParsesAsBefore()
        {
            Assert.That(IniDocument.Parse(ContentText).Write(), Is.EqualTo(ContentText));
            Assert.That(IniDocument.Parse(DefinitionsText).Write(), Is.EqualTo(DefinitionsText));
        }

        [Test]
        public void ByteOrderMarkIsTheOnlyThingDropped()
        {
            Assert.That(IniDocument.Parse(Bom + ContentText).Write(), Is.EqualTo(ContentText));
            Assert.That(IniDocument.Parse(Bom + DefinitionsText).Write(), Is.EqualTo(DefinitionsText));
        }

        [Test]
        public void RepeatedByteOrderMarkIsSkipped()
        {
            // A tool that keeps a decoded mark and writes its own produces two in a row.
            Assert.That(IniDocument.Parse(Bom + Bom + ContentText).Write(), Is.EqualTo(ContentText));
        }

        [Test]
        public void ByteOrderMarkCharacterInsideValueIsKept()
        {
            var doc = IniDocument.Parse("[en-US]\nk=a" + Bom + "b\n");

            Assert.That(doc.TryGetSection("en-US", out var section), Is.True);
            Assert.That(section["k"], Is.EqualTo("a" + Bom + "b"), "only a leading mark is dropped");
        }

        [Test]
        public void DirectorySourceLoadsTablesWithByteOrderMark()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pf_loc_bom_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                WriteWithByteOrderMark(Path.Combine(dir, LocalizationConstants.DefinitionsFilePrefix + LocalizationConstants.PlainExtension), DefinitionsText);
                WriteWithByteOrderMark(Path.Combine(dir, LocalizationConstants.ContentFilePrefix + LocalizationConstants.PlainExtension), ContentText);

                var src = new DirectoryLocalizationSource(dir);

                Assert.That(src.Definitions.ParametersFor("count").Count, Is.EqualTo(1), "definitions: first section loads");
                Assert.That(src.AvailableLanguages, Is.EqualTo(new[] { En, Tr }), "content: every language section loads");
                Assert.That(src.Load(En)["hello"], Is.EqualTo("Hello"), "content: first section entries load");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static void WriteWithByteOrderMark(string path, string text)
        {
            byte[] body = Encoding.UTF8.GetBytes(text); // GetBytes never emits the mark itself
            var bytes = new byte[body.Length + 3];
            bytes[0] = 0xEF; bytes[1] = 0xBB; bytes[2] = 0xBF;
            Buffer.BlockCopy(body, 0, bytes, 3, body.Length);
            File.WriteAllBytes(path, bytes);
        }
    }
}
