using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;

// NUnit coverage for the v2 featureset (parameters, tags, formatters, number abbreviation, INI,
// selection, CSV, table builder, and the rich catalog): one fixture per area. Each test reproduces the
// state the check needs and keeps the original check name as its assertion message.
namespace PFound.LocalizationService.Tests
{
    /// <summary>Localizable enums shared by the fixtures below; their keys derive from the type name.</summary>
    internal static class TestEnums
    {
        [LocalizableEnum]
        internal enum Status { Idle, Active }

        [LocalizableEnum("clr")]
        internal enum Color { Red, Green }
    }

    [TestFixture]
    public sealed class DiagnosticsTests
    {
        private static readonly LocalizationKey Greeting = new LocalizationKey("greeting");

        // Pseudo-localization on, show-keys off, expansion factor 0.4 (5 chars -> 7).
        private static LocalizationDiagnostics CreatePseudoDiagnostics()
            => new LocalizationDiagnostics { PseudoLocalize = true, ExpansionFactor = 0.4f };

        [Test]
        public void PassthroughWhenDisabled()
        {
            var diag = new LocalizationDiagnostics();
            Assert.That(diag.Apply(Greeting, "Hello"), Is.EqualTo("Hello"), "diag: passthrough when disabled");
        }

        [Test]
        public void ShowKeysReturnsTheKeyAndWinsOverPseudo()
        {
            var diag = new LocalizationDiagnostics { ShowKeys = true, PseudoLocalize = true };
            Assert.That(diag.Apply(Greeting, "Hello"), Is.EqualTo("greeting"), "diag: show-keys returns the key");
        }

        [Test]
        public void PseudoPadsByExpansionFactor()
        {
            string pseudo = CreatePseudoDiagnostics().Apply(Greeting, "Hello");
            Assert.That(pseudo.Length, Is.EqualTo(7), "diag: pseudo pads to length*(1+factor)  (5 -> 7)");
        }

        [Test]
        public void PseudoRemapsAsciiToAccentedLookAlikes()
        {
            string pseudo = CreatePseudoDiagnostics().Apply(Greeting, "Hello");
            Assert.That(pseudo, Is.Not.EqualTo("Hello"), "diag: pseudo remaps ASCII to accented look-alikes");
            Assert.That(pseudo.IndexOf("Hello", StringComparison.Ordinal), Is.LessThan(0), "diag: pseudo remaps ASCII to accented look-alikes");
        }

        [Test]
        public void PseudoAppendsThePaddingGlyph()
        {
            string pseudo = CreatePseudoDiagnostics().Apply(Greeting, "Hello");
            Assert.That(pseudo[pseudo.Length - 1], Is.EqualTo('~'), "diag: pseudo appends the padding glyph");
        }

        [Test]
        public void PseudoIsDeterministic()
        {
            var diag = CreatePseudoDiagnostics();
            string pseudo = diag.Apply(Greeting, "Hello");
            Assert.That(diag.Pseudoize("Hello"), Is.EqualTo(pseudo), "diag: pseudo is deterministic");
        }

        [Test]
        public void ExpansionFactorScalesPadding()
        {
            var diag = CreatePseudoDiagnostics();
            diag.ExpansionFactor = 1f;
            Assert.That(diag.Pseudoize("Hello").Length, Is.EqualTo(10), "diag: expansion factor scales padding");
        }

        [Test]
        public void NegativeExpansionFactorClampedToZero()
        {
            var diag = CreatePseudoDiagnostics();
            diag.ExpansionFactor = -5f;
            Assert.That(diag.ExpansionFactor, Is.EqualTo(0f), "diag: negative expansion factor clamped to zero");
        }

        [Test]
        public void PseudoPassesThroughEmptyText()
        {
            var diag = CreatePseudoDiagnostics();
            diag.ExpansionFactor = -5f; // clamped to zero
            Assert.That(diag.Pseudoize(""), Is.EqualTo(""), "diag: pseudo passes through empty text");
        }

#if PFOUND_LOCALIZATION_QA
        // Facade integration (only compiled in QA builds).
        private static LocalizationCatalog CreateQaCatalog()
        {
            var source = new InMemoryLocalizationSource().Add(new LanguageKey("en-US"),
                new Dictionary<string, string> { ["greeting"] = "Hello" });
            var cat = new LocalizationCatalog(source, new LanguageKey("en-US"));
            cat.SetCulture(CultureInfo.InvariantCulture);
            return cat;
        }

        [Test]
        public void FacadeLookupUnaffectedWhenQaOff()
        {
            var cat = CreateQaCatalog();
            Assert.That(cat.Get("greeting"), Is.EqualTo("Hello"), "diag(facade): normal lookup unaffected when QA off");
        }

        [Test]
        public void FacadeShowKeysOverridesResolvedValue()
        {
            var cat = CreateQaCatalog();
            cat.Diagnostics.ShowKeys = true;
            Assert.That(cat.Get("greeting"), Is.EqualTo("greeting"), "diag(facade): show-keys overrides the resolved value");
        }

        [Test]
        public void FacadePseudoLocalizationAppliedToResolvedValue()
        {
            var cat = CreateQaCatalog();
            cat.Diagnostics.PseudoLocalize = true;
            Assert.That(cat.Get("greeting").Length, Is.EqualTo(7), "diag(facade): pseudo-localization applied to resolved value");
        }
#endif
    }

    [TestFixture]
    public sealed class IniValidationTests
    {
        [Test]
        public void WhitespacePaddedValueThrows()
        {
            var d = new IniDocument();
            d.Section("en-US")["k"] = "a\tb   ";
            Assert.Throws<InvalidOperationException>(() => d.Write(), "ini-validate: whitespace-padded value throws");
        }

        [Test]
        public void ValueContainingStoredEscapeThrows()
        {
            var d = new IniDocument();
            d.Section("en-US")["k"] = @"has\\escape";
            Assert.Throws<InvalidOperationException>(() => d.Write(), "ini-validate: value containing the stored-escape sequence throws");
        }

        [Test]
        public void MixedNewlinesThrow()
        {
            var d = new IniDocument();
            d.Section("en-US")["k"] = "a\r\nb\rc";
            Assert.Throws<InvalidOperationException>(() => d.Write(), "ini-validate: mixed CRLF/CR newlines throws");
        }

        [Test]
        public void WhitespacePaddedKeyThrows()
        {
            var d = new IniDocument();
            d.Section("en-US")[" k "] = "v";
            Assert.Throws<InvalidOperationException>(() => d.Write(), "ini-validate: whitespace-padded key throws");
        }

        [Test]
        public void MalformedSectionNameThrows()
        {
            var d = new IniDocument();
            d.Section("bad]name")["k"] = "v";
            Assert.Throws<InvalidOperationException>(() => d.Write(), "ini-validate: malformed section name throws");
        }

        [Test]
        public void InternalNewlineValueSerializes()
        {
            // Valid documents (including internal newlines) still serialize cleanly.
            Assert.DoesNotThrow(() => { var d = new IniDocument(); d.Section("en-US")["multi"] = "line1\nline2"; d.Write(); },
                "ini-validate: internal newline value serializes without throwing");
        }
    }

    [TestFixture]
    public sealed class DirectorySourceTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pf_loc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);

            string csv =
                "Key,ParameterDefinition,en-US,tr-TR\n" +
                "hello,,Hello,Merhaba\n" +
                "count,int-n-NumberAbbreviated,You have {n},\n";
            var built = LocalizationTableBuilder.Build(csv);
            File.WriteAllText(
                Path.Combine(_dir, LocalizationConstants.DefinitionsFilePrefix + LocalizationConstants.PlainExtension),
                built.DefinitionsText);
            File.WriteAllText(
                Path.Combine(_dir, LocalizationConstants.ContentFilePrefix + LocalizationConstants.PlainExtension),
                built.ContentText);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Test]
        public void LanguagesDiscovered()
        {
            var src = new DirectoryLocalizationSource(_dir);
            Assert.That(src.AvailableLanguages.Count, Is.EqualTo(2), "directory source: languages discovered");
        }

        [Test]
        public void ContentLoadedPerLanguage()
        {
            var src = new DirectoryLocalizationSource(_dir);
            Assert.That(src.Load(new LanguageKey("tr-TR"))["hello"], Is.EqualTo("Merhaba"), "directory source: content loaded per language");
        }

        [Test]
        public void DefinitionsLoaded()
        {
            var src = new DirectoryLocalizationSource(_dir);
            Assert.That(src.Definitions.ParametersFor("count").Count, Is.EqualTo(1), "directory source: definitions loaded");
        }

        [Test]
        public void EndToEndCatalogGet()
        {
            var src = new DirectoryLocalizationSource(_dir);
            var catalog = new LocalizationCatalog(src, new LanguageKey("en-US"), src.Definitions);
            catalog.SetCulture(CultureInfo.InvariantCulture);
            Assert.That(catalog.Get("count", new NumberValue(2500)), Is.EqualTo("You have 2.5K"), "directory source: end-to-end catalog get");
        }
    }

    [TestFixture]
    public sealed class ParameterParsingTests
    {
        private static ParameterDefinition ParseTwoSpecs()
            => ParameterDefinition.Parse("int-count-NumberAbbreviated;Vector-pos-Coordinates");

        [Test]
        public void ParsesTwoSpecs()
        {
            Assert.That(ParseTwoSpecs().Count, Is.EqualTo(2), "param def parses two specs");
        }

        [Test]
        public void SpecFieldsAndFormat()
        {
            var def = ParseTwoSpecs();
            Assert.That(def[0].Name, Is.EqualTo("count"), "param spec fields + format");
            Assert.That(def[0].TypeName, Is.EqualTo("int"), "param spec fields + format");
            Assert.That(def[0].Format, Is.EqualTo(LocalizationFormat.NumberAbbreviated), "param spec fields + format");
        }

        [Test]
        public void SecondSpecFormat()
        {
            Assert.That(ParseTwoSpecs()[1].Format, Is.EqualTo(LocalizationFormat.Coordinates), "second spec format");
        }

        [Test]
        public void IndexLookupIsOrdinal()
        {
            var def = ParseTwoSpecs();
            Assert.That(def.IndexOf("pos"), Is.EqualTo(1), "param def index lookup (ordinal)");
            Assert.That(def.IndexOf("nope"), Is.EqualTo(-1), "param def index lookup (ordinal)");
        }

        [Test]
        public void EmptyCellGivesEmptyDefinition()
        {
            Assert.That(ParameterDefinition.Parse("").Count, Is.EqualTo(0), "empty param cell -> empty definition");
        }

        [Test]
        public void SpecWithoutFormatIsNone()
        {
            Assert.That(ParameterSpec.TryParse("Type-name", out var s2), Is.True, "spec without format -> None");
            Assert.That(s2.Format, Is.EqualTo(LocalizationFormat.None), "spec without format -> None");
        }

        [Test]
        public void UnknownFormatTokenIsUnsupported()
        {
            Assert.That(ParameterSpec.TryParse("Type-name-BogusFmt", out var s3), Is.True, "unknown format token -> Unsupported");
            Assert.That(s3.Format, Is.EqualTo(LocalizationFormat.Unsupported), "unknown format token -> Unsupported");
        }

        [Test]
        public void SerializeRoundTrips()
        {
            var def = ParseTwoSpecs();
            Assert.That(def.Serialize().Contains("count"), Is.True, "param def serialize round-trips");
            Assert.That(def.Serialize().Contains(";"), Is.True, "param def serialize round-trips");
        }
    }

    [TestFixture]
    public sealed class TagScannerTests
    {
        private static readonly TagResolver Up = (name, o) => o.Append(name.ToUpperInvariant());

        [Test]
        public void EmptyInputAccepted()
        {
            Assert.That(TagScanner.Process("", '{', '}', Up, out var r0), Is.EqualTo(TagScanOutcome.Accepted), "scanner: empty input accepted");
            Assert.That(r0, Is.EqualTo(""), "scanner: empty input accepted");
        }

        [Test]
        public void NoTagsAccepted()
        {
            Assert.That(TagScanner.Process("plain", '{', '}', Up, out var r1), Is.EqualTo(TagScanOutcome.Accepted), "scanner: no tags accepted");
            Assert.That(r1, Is.EqualTo("plain"), "scanner: no tags accepted");
        }

        [Test]
        public void SingleTagSubstituted()
        {
            Assert.That(TagScanner.Process("a{b}c", '{', '}', Up, out var r2), Is.EqualTo(TagScanOutcome.Accepted), "scanner: single tag substituted");
            Assert.That(r2, Is.EqualTo("aBc"), "scanner: single tag substituted");
        }

        [Test]
        public void EmptyTagRejected()
        {
            Assert.That(TagScanner.Process("{}", '{', '}', Up, out _), Is.EqualTo(TagScanOutcome.RejectedEmptyTag), "scanner: empty tag rejected");
        }

        [Test]
        public void NestedOpenRejected()
        {
            Assert.That(TagScanner.Process("{a{b}", '{', '}', Up, out _), Is.EqualTo(TagScanOutcome.RejectedNested), "scanner: nested open rejected");
        }

        [Test]
        public void StrayCloseRejected()
        {
            Assert.That(TagScanner.Process("a}b", '{', '}', Up, out _), Is.EqualTo(TagScanOutcome.RejectedMismatched), "scanner: stray close rejected");
        }

        [Test]
        public void UnterminatedOpenRejected()
        {
            Assert.That(TagScanner.Process("{a", '{', '}', Up, out _), Is.EqualTo(TagScanOutcome.RejectedMismatched), "scanner: unterminated open rejected");
        }
    }

    [TestFixture]
    public sealed class NumberAbbreviatorTests
    {
        [TestCase(999d, "999", "abbrev: <1e3 raw")]
        [TestCase(1000d, "1K", "abbrev: 1000 -> 1K (2nd digit 0)")]
        [TestCase(1500d, "1.5K", "abbrev: 1500 -> 1.5K")]
        [TestCase(1234567d, "1.2M", "abbrev: 1,234,567 -> 1.2M")]
        [TestCase(12345678d, "12M", "abbrev: 12.3M -> 12M (two integer sig digits)")]
        [TestCase(123456789d, "120M", "abbrev: 123M -> 120M")]
        [TestCase(1e9, "1B", "abbrev: 1e9 -> 1B")]
        [TestCase(2.5e12, "2.5T", "abbrev: 2.5e12 -> 2.5T")]
        [TestCase(5e15, "5q", "abbrev: 5e15 -> 5q")]
        [TestCase(1e18, "1Q", "abbrev: 1e18 -> 1Q")]
        [TestCase(-1500d, "-1.5K", "abbrev: negative keeps sign")]
        public void Abbreviate(double value, string expected, string check)
        {
            Assert.That(NumberAbbreviator.Abbreviate(value), Is.EqualTo(expected), check);
        }
    }

    [TestFixture]
    public sealed class FormatterTests
    {
        private sealed class FixedContext : ILocalizationContext
        {
            public CultureInfo Culture => CultureInfo.InvariantCulture;
            public string Localize(LocalizationKey key)
            {
                if (key.Value == LocalizationConstants.HourAbbreviationKey) return "hr";
                if (key.Value == LocalizationConstants.DayAbbreviationKey) return "d";
                return key.Value;
            }
        }

        private sealed class UpperFormatter : IValueFormatter
        {
            public bool TryFormat(ILocalizationValue value, LocalizationFormat format, ILocalizationContext context, StringBuilder output)
            {
                if (format != LocalizationFormat.Unsupported) return false;
                var sb = new StringBuilder(); value.AppendTo(sb);
                output.Append(sb.ToString().ToUpperInvariant());
                return true;
            }
        }

        private static string Format(ILocalizationValue v, LocalizationFormat f) => Format(new FormatterRegistry(), v, f);

        private static string Format(FormatterRegistry reg, ILocalizationValue v, LocalizationFormat f)
        {
            var sb = new StringBuilder(); reg.Format(v, f, new FixedContext(), sb); return sb.ToString();
        }

        [Test]
        public void CurrencyWhole()
        {
            Assert.That(Format(new NumberValue(1234), LocalizationFormat.CurrencyWhole), Is.EqualTo("1,234"), "fmt: currency whole N0");
        }

        [Test]
        public void CurrencyDecimal()
        {
            Assert.That(Format(new NumberValue(1234.5), LocalizationFormat.CurrencyDecimal), Is.EqualTo("1,234.50"), "fmt: currency decimal N2");
        }

        [Test]
        public void HourlyCurrencyUsesLocalizedHour()
        {
            Assert.That(Format(new NumberValue(10), LocalizationFormat.CurrencyHourly), Is.EqualTo("+10/hr"), "fmt: hourly currency + localized hour");
        }

        [Test]
        public void NumberAbbreviated()
        {
            Assert.That(Format(new NumberValue(2000), LocalizationFormat.NumberAbbreviated), Is.EqualTo("2K"), "fmt: number abbreviated");
        }

        [Test]
        public void DurationWithLocalizedDayUnit()
        {
            Assert.That(Format(new DurationValue(new TimeSpan(1, 2, 3, 4)), LocalizationFormat.Duration), Is.EqualTo("1d 02:03:04"), "fmt: duration with localized day-unit label");
        }

        [Test]
        public void DurationSubDay()
        {
            Assert.That(Format(new DurationValue(new TimeSpan(0, 5, 6, 7)), LocalizationFormat.Duration), Is.EqualTo("05:06:07"), "fmt: duration sub-day");
        }

        [Test]
        public void Coordinates()
        {
            Assert.That(Format(new CoordinatesValue(3, 7), LocalizationFormat.Coordinates), Is.EqualTo("X:3 Y:7"), "fmt: coordinates");
        }

        [Test]
        public void Size()
        {
            Assert.That(Format(new CoordinatesValue(3, 7), LocalizationFormat.Size), Is.EqualTo("3x7"), "fmt: size");
        }

        [Test]
        public void BracketedSize()
        {
            Assert.That(Format(new CoordinatesValue(3, 7), LocalizationFormat.BracketedSize), Is.EqualTo("[3x7]"), "fmt: bracketed size");
        }

        [Test]
        public void NoneFallsBackToValueText()
        {
            Assert.That(Format(new TextValue("raw"), LocalizationFormat.None), Is.EqualTo("raw"), "fmt: None falls back to value text");
        }

        [Test]
        public void CustomRegistryConsultedFirst()
        {
            // Custom formatter wins over built-in and is tried in insertion order.
            var reg = new FormatterRegistry();
            reg.Register(new UpperFormatter());
            Assert.That(Format(reg, new TextValue("hi"), LocalizationFormat.Unsupported), Is.EqualTo("HI"), "fmt: custom registry consulted first");
        }
    }

    [TestFixture]
    public sealed class IniTests
    {
        private static string WriteSample()
        {
            var doc = new IniDocument();
            var s = doc.Section("en-US");
            s["greeting"] = "Hello";
            s["multi"] = "line1\nline2";
            return doc.Write();
        }

        private static LocalizationDefinitions RoundTripDefinitions()
        {
            var defs = new LocalizationDefinitions();
            defs.Parameters["k"] = ParameterDefinition.Parse("int-n-NumberAbbreviated");
            defs.DynamicKeys["item_{id}"] = ParameterDefinition.Parse("int-id");
            defs.Languages.Add(new LanguageInfo("en-US", "English"));
            return LocalizationDefinitions.FromIni(defs.ToIni());
        }

        [Test]
        public void NewlineEscapedOnWrite()
        {
            string text = WriteSample();
            Assert.That(text.Contains("[en-US]"), Is.True, "ini: newline escaped on write (two-backslash on-disk form)");
            Assert.That(text.Contains(@"multi=line1\\line2"), Is.True, "ini: newline escaped on write (two-backslash on-disk form)");
        }

        [Test]
        public void RoundTripKey()
        {
            var back = IniDocument.Parse(WriteSample());
            Assert.That(back.TryGetSection("en-US", out var s2), Is.True, "ini: round-trip key");
            Assert.That(s2["greeting"], Is.EqualTo("Hello"), "ini: round-trip key");
        }

        [Test]
        public void NewlineUnescapedOnRead()
        {
            var back = IniDocument.Parse(WriteSample());
            Assert.That(back.TryGetSection("en-US", out var s2), Is.True, "ini: newline unescaped on read");
            Assert.That(s2["multi"], Is.EqualTo("line1\nline2"), "ini: newline unescaped on read");
        }

        [Test]
        public void DefinitionsRoundTrip()
        {
            var parsed = RoundTripDefinitions();
            Assert.That(parsed.ParametersFor("k").Count, Is.EqualTo(1), "ini: definitions round-trip");
            Assert.That(parsed.TryGetDynamicKey("item_{id}", out _), Is.True, "ini: definitions round-trip");
        }

        [Test]
        public void LanguageDefinitionsRoundTrip()
        {
            var parsed = RoundTripDefinitions();
            Assert.That(parsed.Languages.Count, Is.EqualTo(1), "ini: language definitions round-trip");
            Assert.That(parsed.Languages[0].DisplayName, Is.EqualTo("English"), "ini: language definitions round-trip");
        }
    }

    [TestFixture]
    public sealed class RedirectionTests
    {
        [Test]
        public void ExactRedirect()
        {
            var r = LanguageRedirections.CreateDefault();
            Assert.That(r.TryRedirect("en-UK", out var a), Is.True, "redirect: en-UK -> en-GB (exact)");
            Assert.That(a, Is.EqualTo("en-GB"), "redirect: en-UK -> en-GB (exact)");
        }

        [Test]
        public void PrefixRedirect()
        {
            var r = LanguageRedirections.CreateDefault();
            Assert.That(r.TryRedirect("en-AU", out var b), Is.True, "redirect: en-* -> en-US (prefix)");
            Assert.That(b, Is.EqualTo("en-US"), "redirect: en-* -> en-US (prefix)");
        }

        [Test]
        public void UnrelatedCodeNotRedirected()
        {
            var r = LanguageRedirections.CreateDefault();
            Assert.That(r.TryRedirect("tr-TR", out _), Is.False, "redirect: unrelated code not redirected");
        }
    }

    [TestFixture]
    public sealed class SelectionTests
    {
        private static List<LanguageKey> Supported()
            => new List<LanguageKey> { new LanguageKey("en-US"), new LanguageKey("tr-TR"), new LanguageKey("en-GB") };

        [Test]
        public void SupportedPreferenceWins()
        {
            var a = LanguageSelector.Resolve("tr-TR", "en-US", Supported(), LanguageRedirections.CreateDefault(), out bool clearA);
            Assert.That(a.Code, Is.EqualTo("tr-TR"), "select: supported preference wins");
            Assert.That(clearA, Is.False, "select: supported preference wins");
        }

        [Test]
        public void DeviceCultureWhenNoPreference()
        {
            var b = LanguageSelector.Resolve(null, "en-US", Supported(), LanguageRedirections.CreateDefault(), out _);
            Assert.That(b.Code, Is.EqualTo("en-US"), "select: device culture when no preference");
        }

        [Test]
        public void StalePreferenceClearedFallsToDevice()
        {
            var d = LanguageSelector.Resolve("de-DE", "tr-TR", Supported(), LanguageRedirections.CreateDefault(), out bool clearD);
            Assert.That(d.Code, Is.EqualTo("tr-TR"), "select: stale preference cleared, falls to device");
            Assert.That(clearD, Is.True, "select: stale preference cleared, falls to device");
        }

        [Test]
        public void PreferenceRedirectedToSupported()
        {
            var e = LanguageSelector.Resolve("en-UK", "tr-TR", Supported(), LanguageRedirections.CreateDefault(), out _);
            Assert.That(e.Code, Is.EqualTo("en-GB"), "select: preference redirected to supported");
        }

        [Test]
        public void DeviceRedirectedViaPrefix()
        {
            var f = LanguageSelector.Resolve(null, "en-AU", Supported(), LanguageRedirections.CreateDefault(), out _);
            Assert.That(f.Code, Is.EqualTo("en-US"), "select: device redirected via prefix");
        }

        [Test]
        public void DefaultWhenNothingResolves()
        {
            var g = LanguageSelector.Resolve(null, "fr-FR", Supported(), LanguageRedirections.CreateDefault(), out _);
            Assert.That(g.Code, Is.EqualTo(LocalizationConstants.DefaultLanguageCode), "select: default when nothing resolves");
        }
    }

    [TestFixture]
    public sealed class CsvTests
    {
        [Test]
        public void SimpleRow()
        {
            var simple = CsvReader.Parse("a,b,c");
            Assert.That(simple.Count, Is.EqualTo(1), "csv: simple row");
            Assert.That(simple[0].Count, Is.EqualTo(3), "csv: simple row");
            Assert.That(simple[0][2], Is.EqualTo("c"), "csv: simple row");
        }

        [Test]
        public void QuotedCommaPreserved()
        {
            var quoted = CsvReader.Parse("a,\"b,c\",d");
            Assert.That(quoted[0].Count, Is.EqualTo(3), "csv: quoted comma preserved");
            Assert.That(quoted[0][1], Is.EqualTo("b,c"), "csv: quoted comma preserved");
        }

        [Test]
        public void EscapedQuotes()
        {
            var esc = CsvReader.Parse("\"he said \"\"hi\"\"\"");
            Assert.That(esc[0][0], Is.EqualTo("he said \"hi\""), "csv: escaped quotes");
        }

        [Test]
        public void NewlineInsideQuotes()
        {
            var nl = CsvReader.Parse("a,\"line1\nline2\"");
            Assert.That(nl[0][1], Is.EqualTo("line1\nline2"), "csv: newline inside quotes");
        }

        [Test]
        public void TrailingNewlineNoPhantomRecord()
        {
            var trailing = CsvReader.Parse("a,b\n");
            Assert.That(trailing.Count, Is.EqualTo(1), "csv: trailing newline no phantom record");
        }

        [Test]
        public void CrlfSplitsRecords()
        {
            var crlf = CsvReader.Parse("a,b\r\nc,d");
            Assert.That(crlf.Count, Is.EqualTo(2), "csv: CRLF splits records");
            Assert.That(crlf[1][0], Is.EqualTo("c"), "csv: CRLF splits records");
        }
    }

    [TestFixture]
    public sealed class TableBuilderTests
    {
        private static LocalizationTableBuilder.Result Build()
        {
            string csv =
                "Key,ParameterDefinition,en-US,tr-TR\n" +
                "greeting,,Hello,Merhaba\n" +
                "count,int-n-NumberAbbreviated,You have {n},\n" +
                "item_{id}_name,int-id,Sword,\n";
            return LocalizationTableBuilder.Build(csv, new[] { typeof(TestEnums.Status) });
        }

        private static LocalizationDefinitions BuildDefinitions()
            => LocalizationDefinitions.FromIni(IniDocument.Parse(Build().DefinitionsText));

        private static IReadOnlyDictionary<string, string> BuildContent(string language)
            => ContentTables.FromText(Build().ContentText).Load(new LanguageKey(language));

        [Test]
        public void TwoLanguagesFromHeader()
        {
            Assert.That(Build().Languages.Count, Is.EqualTo(2), "builder: two languages from header");
        }

        [Test]
        public void ParamKeyRoutedToParameterDefinitions()
        {
            Assert.That(BuildDefinitions().ParametersFor("count").Count, Is.EqualTo(1), "builder: param key routed to ParameterDefinitions");
        }

        [Test]
        public void DynamicKeyRoutedToDynamicKeyDefinitions()
        {
            Assert.That(BuildDefinitions().TryGetDynamicKey("item_{id}_name", out _), Is.True, "builder: dynamic key routed to DynamicKeyDefinitions");
        }

        [Test]
        public void EmptyParamCellNotStored()
        {
            Assert.That(BuildDefinitions().Parameters.ContainsKey("greeting"), Is.False, "builder: empty param cell not stored");
        }

        [Test]
        public void ContentPerLanguage()
        {
            Assert.That(BuildContent("en-US")["greeting"], Is.EqualTo("Hello"), "builder: content per language");
            Assert.That(BuildContent("tr-TR")["greeting"], Is.EqualTo("Merhaba"), "builder: content per language");
        }

        [Test]
        public void EmptyTranslationCellSkipped()
        {
            Assert.That(BuildContent("tr-TR").ContainsKey("count"), Is.False, "builder: empty translation cell skipped");
        }

        [Test]
        public void EnumKeysAutoAddedWithPlaceholder()
        {
            var en = BuildContent("en-US");
            Assert.That(en.ContainsKey("Status_Idle"), Is.True, "builder: enum keys auto-added with placeholder");
            Assert.That(en["Status_Active"], Is.EqualTo(LocalizationConstants.MissingEnumText), "builder: enum keys auto-added with placeholder");
        }

        [Test]
        public void EnumKeysAddedInEveryLanguage()
        {
            Assert.That(BuildContent("tr-TR").ContainsKey("Status_Active"), Is.True, "builder: enum keys added in every language");
        }
    }

    [TestFixture]
    public sealed class EnumKeyTests
    {
        [Test]
        public void DefaultPrefixIsTypeName()
        {
            Assert.That(EnumKeyGenerator.KeyFor(TestEnums.Status.Active), Is.EqualTo("Status_Active"), "enum: default prefix is type name");
        }

        [Test]
        public void AttributePrefixOverride()
        {
            Assert.That(EnumKeyGenerator.KeyFor(TestEnums.Color.Red), Is.EqualTo("clr_Red"), "enum: attribute prefix override");
        }

        [Test]
        public void IsLocalizableGate()
        {
            Assert.That(EnumKeyGenerator.IsLocalizable(typeof(TestEnums.Status)), Is.True, "enum: IsLocalizable gate");
            Assert.That(EnumKeyGenerator.IsLocalizable(typeof(int)), Is.False, "enum: IsLocalizable gate");
        }

        [Test]
        public void EnumValueResolvesToItsKey()
        {
            ILocalizationValue v = new EnumValue(TestEnums.Color.Green);
            Assert.That(v.TryGetLocalizationKey(out var k), Is.True, "enum value: resolves to its key");
            Assert.That(k.Value, Is.EqualTo("clr_Green"), "enum value: resolves to its key");
        }

        [Test]
        public void EnumValueExposesNumericValue()
        {
            ILocalizationValue v = new EnumValue(TestEnums.Color.Green);
            Assert.That(v.TryGetDouble(out double d), Is.True, "enum value: exposes numeric value");
            Assert.That(d, Is.EqualTo(1d), "enum value: exposes numeric value");
        }
    }

    [TestFixture]
    public sealed class CatalogTests
    {
        private static readonly LanguageKey En = new LanguageKey("en-US");
        private static readonly LanguageKey Tr = new LanguageKey("tr-TR");

        private Action<string> _savedOnWarning;
        private Action<string> _savedOnError;

        private sealed class RecordingResolver : IValueKeyResolver
        {
            public bool TryResolveKey(ILocalizationValue value, out LocalizationKey key)
            {
                // Route any number 42 to a special key, to prove the hook beats formatting.
                if (value.TryGetDouble(out double d) && d == 42) { key = new LocalizationKey("answer"); return true; }
                key = default; return false;
            }
        }

        // GetOrErrorText logs by design. The log sink is static: silence it here (its default) so a sink
        // another test wired to the engine console cannot turn that log into a test failure.
        [SetUp]
        public void SetUp()
        {
            _savedOnWarning = LocalizationLog.OnWarning;
            _savedOnError = LocalizationLog.OnError;
            LocalizationLog.OnWarning = null;
            LocalizationLog.OnError = null;
        }

        [TearDown]
        public void TearDown()
        {
            LocalizationLog.OnWarning = _savedOnWarning;
            LocalizationLog.OnError = _savedOnError;
        }

        private static LocalizationCatalog CreateCatalog()
        {
            var source = new InMemoryLocalizationSource()
                .Add(En, new Dictionary<string, string>
                {
                    ["hello"] = "Hello",
                    ["gold"] = "You own {amount}",
                    ["pos"] = "At {loc}",
                    ["state"] = "State: {s}",
                    ["Status_Active"] = "ACTIVE",
                    ["answer"] = "forty-two",
                    ["pick"] = "Chosen: {v}",
                    ["item_5_name"] = "Sword",
                    ["only_en"] = "OnlyEN",
                })
                .Add(Tr, new Dictionary<string, string>
                {
                    ["hello"] = "Merhaba",
                });

            var defs = new LocalizationDefinitions();
            defs.Parameters["gold"] = ParameterDefinition.Parse("int-amount-NumberAbbreviated");
            defs.Parameters["pos"] = ParameterDefinition.Parse("Vec-loc-Coordinates");
            defs.Parameters["state"] = ParameterDefinition.Parse("Enum-s");
            defs.Parameters["pick"] = ParameterDefinition.Parse("Num-v-CurrencyWhole");
            defs.DynamicKeys["item_{id}_name"] = ParameterDefinition.Parse("int-id");

            var catalog = new LocalizationCatalog(source, En, defs);
            catalog.SetCulture(CultureInfo.InvariantCulture);
            return catalog;
        }

        private static LocalizationCatalog CreateCatalogWithResolver()
        {
            var catalog = CreateCatalog();
            catalog.SetValueKeyResolver(new RecordingResolver());
            return catalog;
        }

        private static LocalizationCatalog CreateCatalogWithResolverInTurkish()
        {
            var catalog = CreateCatalogWithResolver();
            catalog.SwitchLanguage(Tr);
            return catalog;
        }

        [Test]
        public void PlainGet()
        {
            Assert.That(CreateCatalog().Get("hello"), Is.EqualTo("Hello"), "catalog: plain get");
        }

        [Test]
        public void NumberAbbreviatedTag()
        {
            Assert.That(CreateCatalog().Get("gold", new NumberValue(1500000)), Is.EqualTo("You own 1.5M"), "catalog: number-abbreviated tag");
        }

        [Test]
        public void CoordinatesTag()
        {
            Assert.That(CreateCatalog().Get("pos", new CoordinatesValue(3, 7)), Is.EqualTo("At X:3 Y:7"), "catalog: coordinates tag");
        }

        [Test]
        public void EnumValueKeyBeatsFormatting()
        {
            Assert.That(CreateCatalog().Get("state", new EnumValue(TestEnums.Status.Active)), Is.EqualTo("State: ACTIVE"), "catalog: value-key (enum) beats formatting");
        }

        [Test]
        public void DynamicKeySubstitution()
        {
            Assert.That(CreateCatalog().Get("item_{id}_name", new TextValue("5")), Is.EqualTo("Sword"), "catalog: dynamic key substitution");
        }

        [Test]
        public void ValueKeyResolverHookWins()
        {
            Assert.That(CreateCatalogWithResolver().Get("pick", new NumberValue(42)), Is.EqualTo("Chosen: forty-two"), "catalog: value-key resolver hook wins");
        }

        [Test]
        public void FallsToFormatWhenHookDeclines()
        {
            Assert.That(CreateCatalogWithResolver().Get("pick", new NumberValue(7)), Is.EqualTo("Chosen: 7"), "catalog: falls to format when hook declines");
        }

        [Test]
        public void TryGetFalseWhenMissing()
        {
            Assert.That(CreateCatalogWithResolver().TryGet("nope", out _), Is.False, "catalog: TryGet false when missing");
        }

        [Test]
        public void FallbackAwareTryGetReportsFallback()
        {
            var catalog = CreateCatalogWithResolverInTurkish();
            Assert.That(catalog.TryGet("only_en", out var t, out bool fromFb), Is.True, "catalog: fallback-aware reports fallback");
            Assert.That(fromFb, Is.True, "catalog: fallback-aware reports fallback");
            Assert.That(t, Is.EqualTo("OnlyEN"), "catalog: fallback-aware reports fallback");
        }

        [Test]
        public void GetEnsuredThrowsWhenMissing()
        {
            var catalog = CreateCatalogWithResolverInTurkish();
            Assert.Throws<KeyNotFoundException>(() => catalog.GetEnsured("nope"), "catalog: GetEnsured throws when missing");
        }

        [Test]
        public void GetOrErrorTextPlaceholder()
        {
            Assert.That(CreateCatalogWithResolverInTurkish().GetOrErrorText("nope"), Is.EqualTo("# [nope] #"), "catalog: GetOrErrorText placeholder");
        }

        [Test]
        public void GetReturnsRawKeyWhenMissing()
        {
            Assert.That(CreateCatalogWithResolverInTurkish().Get("nope"), Is.EqualTo("nope"), "catalog: Get returns raw key when missing");
        }
    }
}
