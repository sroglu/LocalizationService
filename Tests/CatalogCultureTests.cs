using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace PFound.LocalizationService.Tests
{
    /// <summary>
    /// The catalog formats and parses parameter values under its own culture, whatever the thread culture
    /// is: the built-in formats, a value's plain-text fallback, text read as a number and dynamic-key
    /// parameters. Outside a lookup the values use the thread culture again.
    /// </summary>
    [TestFixture]
    public sealed class CatalogCultureTests
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
        private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

        private CultureInfo _savedCulture;

        private sealed class ThrowingFormatter : IValueFormatter
        {
            public bool TryFormat(ILocalizationValue value, LocalizationFormat format, ILocalizationContext context, StringBuilder output)
                => throw new InvalidOperationException("formatter failure");
        }

        [SetUp]
        public void SetUp()
        {
            _savedCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = En;
            Assume.That(1.5.ToString(Tr), Is.Not.EqualTo(1.5.ToString(En)), "tr-TR and en-US must format 1.5 differently");
        }

        [TearDown]
        public void TearDown() => CultureInfo.CurrentCulture = _savedCulture;

        private static LocalizationCatalog CreateTurkishCatalog(FormatterRegistry formatters = null)
        {
            var tr = new LanguageKey("tr-TR");
            var source = new InMemoryLocalizationSource().Add(tr, new Dictionary<string, string>
            {
                ["plain"] = "Value {v}",
                ["parsed"] = "Total {t}",
                ["dyn_1,5"] = "Dynamic tr",
                ["dyn_1.5"] = "Dynamic en",
            });
            var defs = new LocalizationDefinitions();
            defs.Parameters["plain"] = ParameterDefinition.Parse("Num-v");
            defs.Parameters["parsed"] = ParameterDefinition.Parse("Num-t-CurrencyDecimal");
            defs.DynamicKeys["dyn_{id}"] = ParameterDefinition.Parse("Num-id");
            return new LocalizationCatalog(source, tr, defs, formatters, Tr);
        }

        [Test]
        public void ValuesRenderUnderTheCatalogCultureNotTheThreadCulture()
        {
            var catalog = CreateTurkishCatalog();

            Assert.That(catalog.Get("plain", new NumberValue(1.5)), Is.EqualTo("Value " + 1.5.ToString(Tr)), "plain-text fallback");
            Assert.That(catalog.Get("parsed", new TextValue("1,5")), Is.EqualTo("Total " + 1.5.ToString("N2", Tr)), "text read as a number");
            Assert.That(catalog.Get("dyn_{id}", new NumberValue(1.5)), Is.EqualTo("Dynamic tr"), "dynamic-key parameter");
            Assert.That(CultureInfo.CurrentCulture.Name, Is.EqualTo("en-US"), "the thread culture is untouched");
        }

        [Test]
        public void ValuesUseTheThreadCultureOutsideALookup()
        {
            CreateTurkishCatalog().Get("plain", new NumberValue(1.5));

            var sb = new StringBuilder();
            new NumberValue(1.5).AppendTo(sb);
            Assert.That(sb.ToString(), Is.EqualTo(1.5.ToString(En)));
            Assert.That(new TextValue("1.5").TryGetDouble(out double parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(1.5));
        }

        [Test]
        public void ScopeEndsWhenAFormatterThrows()
        {
            var formatters = new FormatterRegistry();
            formatters.Register(new ThrowingFormatter());
            var catalog = CreateTurkishCatalog(formatters);

            Assert.Throws<InvalidOperationException>(() => catalog.Get("plain", new NumberValue(1.5)));

            var sb = new StringBuilder();
            new NumberValue(1.5).AppendTo(sb);
            Assert.That(sb.ToString(), Is.EqualTo(1.5.ToString(En)), "values are back on the thread culture");
        }
    }
}
