using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace PFound.LocalizationService.Unity.Tests
{
    /// <summary>
    /// <see cref="UnityLocalizationController.ActivateStartupLanguage"/> with and without a configured
    /// default language. The device culture is the thread's current culture, so each test sets it; the
    /// teardown restores the cultures and the log sinks, which the controller changes process-wide.
    /// </summary>
    [TestFixture]
    public sealed class UnityLocalizationControllerTests
    {
        private sealed class MemoryPreferenceStore : ILanguagePreferenceStore
        {
            public string Stored;
            public int ClearCount;

            public MemoryPreferenceStore(string stored = null) { Stored = stored; }

            public bool TryGet(out string code) { code = Stored; return !string.IsNullOrEmpty(code); }
            public void Set(string code) { Stored = code; }
            public void Clear() { Stored = null; ClearCount++; }
        }

        private CultureInfo _culture;
        private CultureInfo _uiCulture;
        private CultureInfo _defaultThreadCulture;
        private CultureInfo _defaultThreadUiCulture;
        private Action<string> _onWarning;
        private Action<string> _onError;

        [SetUp]
        public void SetUp()
        {
            _culture = CultureInfo.CurrentCulture;
            _uiCulture = CultureInfo.CurrentUICulture;
            _defaultThreadCulture = CultureInfo.DefaultThreadCurrentCulture;
            _defaultThreadUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
            _onWarning = LocalizationLog.OnWarning;
            _onError = LocalizationLog.OnError;
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.DefaultThreadCurrentCulture = _defaultThreadCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _defaultThreadUiCulture;
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
            LocalizationLog.OnWarning = _onWarning;
            LocalizationLog.OnError = _onError;
        }

        private static void SetDeviceCulture(string code) => CultureInfo.CurrentCulture = new CultureInfo(code);

        // en-GB and tr-TR, no en-US; en-GB is the catalog's fallback.
        private static LocalizationCatalog CatalogWithoutEnUs()
        {
            var source = new InMemoryLocalizationSource()
                .Add(new LanguageKey("en-GB"), new Dictionary<string, string> { ["hello"] = "Hello" })
                .Add(new LanguageKey("tr-TR"), new Dictionary<string, string> { ["hello"] = "Merhaba" });
            return new LocalizationCatalog(source, new LanguageKey("en-GB"));
        }

        private static LocalizationCatalog CatalogWithEnUs()
        {
            var source = new InMemoryLocalizationSource()
                .Add(new LanguageKey("en-US"), new Dictionary<string, string> { ["hello"] = "Hello" })
                .Add(new LanguageKey("en-GB"), new Dictionary<string, string> { ["hello"] = "Hello" })
                .Add(new LanguageKey("tr-TR"), new Dictionary<string, string> { ["hello"] = "Merhaba" });
            return new LocalizationCatalog(source, new LanguageKey("en-US"));
        }

        [Test]
        public void UnresolvableDeviceCultureActivatesTheConfiguredDefault()
        {
            SetDeviceCulture("fr-FR");
            var store = new MemoryPreferenceStore();
            var controller = new UnityLocalizationController(CatalogWithoutEnUs(), store, new LanguageKey("en-GB"));

            LanguageKey chosen = default;
            Assert.DoesNotThrow(() => chosen = controller.ActivateStartupLanguage());

            Assert.That(chosen.Code, Is.EqualTo("en-GB"));
            Assert.That(controller.ActiveLanguage.Code, Is.EqualTo("en-GB"));
            Assert.That(controller.Catalog.Get("hello"), Is.EqualTo("Hello"));
            Assert.That(store.ClearCount, Is.EqualTo(0));
        }

        [Test]
        public void DeviceCultureRedirectedToMissingEnUsActivatesTheConfiguredDefault()
        {
            SetDeviceCulture("en-AU"); // the built-in en-* rule redirects it to en-US, which the tables lack
            var controller = new UnityLocalizationController(CatalogWithoutEnUs(), new MemoryPreferenceStore(), new LanguageKey("en-GB"));

            LanguageKey chosen = default;
            Assert.DoesNotThrow(() => chosen = controller.ActivateStartupLanguage());

            Assert.That(chosen.Code, Is.EqualTo("en-GB"));
            Assert.That(controller.ActiveLanguage.Code, Is.EqualTo("en-GB"));
        }

        [Test]
        public void StalePreferenceIsClearedAndTheConfiguredDefaultActivated()
        {
            SetDeviceCulture("fr-FR");
            var store = new MemoryPreferenceStore("de-DE");
            var controller = new UnityLocalizationController(CatalogWithoutEnUs(), store, new LanguageKey("en-GB"));

            LanguageKey chosen = default;
            Assert.DoesNotThrow(() => chosen = controller.ActivateStartupLanguage());

            Assert.That(store.ClearCount, Is.EqualTo(1), "the stale preference is cleared");
            Assert.That(store.Stored, Is.Null);
            Assert.That(chosen.Code, Is.EqualTo("en-GB"));
            Assert.That(controller.ActiveLanguage.Code, Is.EqualTo("en-GB"));
        }

        [Test]
        public void ConfiguredDefaultMissingFromTheTablesFailsNamingTheCode()
        {
            var catalog = CatalogWithoutEnUs();

            var ex = Assert.Throws<ArgumentException>(
                () => new UnityLocalizationController(catalog, new MemoryPreferenceStore(), new LanguageKey("de-DE")));

            Assert.That(ex.Message, Does.Contain("de-DE"));
            Assert.That(LocalizationLog.OnError, Is.SameAs(_onError), "it fails before rewiring the log sinks");
        }

        [TestCase(null, "fr-FR", "en-US", 0)]    // last resort: the built-in en-US
        [TestCase(null, "en-AU", "en-US", 0)]    // device culture redirected by the en-* rule
        [TestCase(null, "tr-TR", "tr-TR", 0)]    // device culture supported directly
        [TestCase("tr-TR", "fr-FR", "tr-TR", 0)] // supported preference wins
        [TestCase("de-DE", "fr-FR", "en-US", 1)] // stale preference cleared, then the last resort
        [TestCase("de-DE", "en-GB", "en-GB", 1)] // stale preference cleared, then the device culture
        public void WithoutConfiguredDefaultOutcomesAreUnchanged(string preferred, string deviceCulture, string expected, int expectedClears)
        {
            SetDeviceCulture(deviceCulture);
            var store = new MemoryPreferenceStore(preferred);
            var controller = new UnityLocalizationController(CatalogWithEnUs(), store);

            Assert.That(controller.ActivateStartupLanguage().Code, Is.EqualTo(expected));
            Assert.That(controller.ActiveLanguage.Code, Is.EqualTo(expected));
            Assert.That(store.ClearCount, Is.EqualTo(expectedClears));
        }

        [Test]
        public void WithoutConfiguredDefaultTablesLackingEnUsStillFailAsBefore()
        {
            SetDeviceCulture("fr-FR");
            var controller = new UnityLocalizationController(CatalogWithoutEnUs(), new MemoryPreferenceStore());

            var ex = Assert.Throws<ArgumentException>(() => controller.ActivateStartupLanguage());

            Assert.That(ex.Message, Does.Contain(LocalizationConstants.DefaultLanguageCode));
        }
    }
}
