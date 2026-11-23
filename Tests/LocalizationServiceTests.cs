using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace PFound.LocalizationService.Tests
{
    /// <summary>
    /// The localization spine (<see cref="LocalizationService"/>): active/fallback resolution, language
    /// switching and the raw-key fallback. Each test builds its own service and reproduces the language
    /// state the check needs; the assertion messages keep the original check names.
    /// </summary>
    [TestFixture]
    public sealed class LocalizationServiceTests
    {
        private static readonly LanguageKey En = new LanguageKey("en");
        private static readonly LanguageKey Tr = new LanguageKey("tr");

        private sealed class MemorySource : ILocalizationSource
        {
            private readonly Dictionary<LanguageKey, IReadOnlyDictionary<string, string>> _data;
            public MemorySource(Dictionary<LanguageKey, IReadOnlyDictionary<string, string>> data) { _data = data; }
            public IReadOnlyList<LanguageKey> AvailableLanguages
            {
                get { var l = new List<LanguageKey>(_data.Keys); return l; }
            }
            public IReadOnlyDictionary<string, string> Load(LanguageKey language) => _data[language];
        }

        private static LocalizationService CreateService()
        {
            var source = new MemorySource(new Dictionary<LanguageKey, IReadOnlyDictionary<string, string>>
            {
                [En] = new Dictionary<string, string> { ["hello"] = "Hello", ["greet"] = "Hi {0}", ["only_en"] = "OnlyEN" },
                [Tr] = new Dictionary<string, string> { ["hello"] = "Merhaba", ["greet"] = "Selam {0}" }, // no "only_en"
            });
            return new LocalizationService(source, fallbackLanguage: En);
        }

        private static LocalizationService CreateServiceSwitchedToTurkish()
        {
            var loc = CreateService();
            loc.SwitchLanguage(Tr);
            return loc;
        }

        [Test]
        public void ActiveLanguageDefaultsToFallback()
        {
            var loc = CreateService();
            Assert.That(loc.ActiveLanguage, Is.EqualTo(En), "active language defaults to fallback");
        }

        [Test]
        public void GetReturnsActiveLanguageText()
        {
            var loc = CreateService();
            Assert.That(loc.Get("hello"), Is.EqualTo("Hello"), "Get returns active-language text");
        }

        [Test]
        public void SwitchLanguageChangesResolution()
        {
            var loc = CreateServiceSwitchedToTurkish();
            Assert.That(loc.ActiveLanguage, Is.EqualTo(Tr), "SwitchLanguage changes resolution");
            Assert.That(loc.Get("hello"), Is.EqualTo("Merhaba"), "SwitchLanguage changes resolution");
        }

        [Test]
        public void GetFormatsWithArgs()
        {
            var loc = CreateServiceSwitchedToTurkish();
            Assert.That(loc.Get("greet", "Ali"), Is.EqualTo("Selam Ali"), "Get formats with args");
        }

        [Test]
        public void MissingKeyFallsBackToFallbackLanguage()
        {
            var loc = CreateServiceSwitchedToTurkish();
            Assert.That(loc.Get("only_en"), Is.EqualTo("OnlyEN"), "missing key falls back to fallback language");
        }

        [Test]
        public void MissingEverywhereReturnsRawKey()
        {
            var loc = CreateServiceSwitchedToTurkish();
            Assert.That(loc.Get("does_not_exist"), Is.EqualTo("does_not_exist"), "missing everywhere returns the raw key");
        }

        [Test]
        public void IsLanguageSupported()
        {
            var loc = CreateServiceSwitchedToTurkish();
            Assert.That(loc.IsLanguageSupported(Tr), Is.True, "IsLanguageSupported");
            Assert.That(loc.IsLanguageSupported(new LanguageKey("de")), Is.False, "IsLanguageSupported");
        }

        [Test]
        public void SupportedLanguagesListsSourceLanguages()
        {
            var loc = CreateServiceSwitchedToTurkish();
            Assert.That(loc.SupportedLanguages.Count, Is.EqualTo(2), "SupportedLanguages lists source languages");
        }

        [Test]
        public void TryGetUnprocessedReturnsUnformattedText()
        {
            var loc = CreateServiceSwitchedToTurkish();
            Assert.That(loc.TryGetUnprocessed("greet", out var raw), Is.True, "TryGetUnprocessed returns unformatted text");
            Assert.That(raw, Is.EqualTo("Selam {0}"), "TryGetUnprocessed returns unformatted text");
        }

        [Test]
        public void SwitchLanguageThrowsForUnsupportedLanguage()
        {
            var loc = CreateServiceSwitchedToTurkish();
            Assert.Throws<ArgumentException>(() => loc.SwitchLanguage(new LanguageKey("de")),
                "SwitchLanguage throws for unsupported language");
        }
    }
}
