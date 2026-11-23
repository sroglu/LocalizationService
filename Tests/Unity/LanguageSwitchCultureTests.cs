using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace PFound.LocalizationService.Unity.Tests
{
    /// <summary>Saves the four culture settings and the log sinks, sets a known culture, restores them after.</summary>
    public abstract class CultureSandbox
    {
        protected static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
        protected static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        private CultureInfo _culture;
        private CultureInfo _uiCulture;
        private CultureInfo _defaultThreadCulture;
        private CultureInfo _defaultThreadUiCulture;
        private Action<string> _onWarning;
        private Action<string> _onError;

        protected sealed class MemoryPreferenceStore : ILanguagePreferenceStore
        {
            public string Stored;
            public MemoryPreferenceStore(string stored = null) { Stored = stored; }
            public bool TryGet(out string code) { code = Stored; return !string.IsNullOrEmpty(code); }
            public void Set(string code) { Stored = code; }
            public void Clear() { Stored = null; }
        }

        [SetUp]
        public void SaveAndSetKnownCulture()
        {
            _culture = CultureInfo.CurrentCulture;
            _uiCulture = CultureInfo.CurrentUICulture;
            _defaultThreadCulture = CultureInfo.DefaultThreadCurrentCulture;
            _defaultThreadUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
            _onWarning = LocalizationLog.OnWarning;
            _onError = LocalizationLog.OnError;

            CultureInfo.CurrentCulture = En;
            CultureInfo.CurrentUICulture = En;
            CultureInfo.DefaultThreadCurrentCulture = En;
            CultureInfo.DefaultThreadCurrentUICulture = En;
        }

        [TearDown]
        public void Restore()
        {
            CultureInfo.DefaultThreadCurrentCulture = _defaultThreadCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _defaultThreadUiCulture;
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
            LocalizationLog.OnWarning = _onWarning;
            LocalizationLog.OnError = _onError;
        }
    }

    /// <summary>
    /// A language switch, and the startup activation, change the catalog's culture only: the thread and
    /// process cultures keep the values they had.
    /// </summary>
    [TestFixture]
    public sealed class LanguageSwitchCultureTests : CultureSandbox
    {
        private static LocalizationCatalog CreateCatalog()
        {
            var source = new InMemoryLocalizationSource()
                .Add(new LanguageKey("en-US"), new Dictionary<string, string> { ["hello"] = "Hello" })
                .Add(new LanguageKey("tr-TR"), new Dictionary<string, string> { ["hello"] = "Merhaba" })
                .Add(new LanguageKey("pseudo"), new Dictionary<string, string> { ["hello"] = "[Hello]" });
            return new LocalizationCatalog(source, new LanguageKey("en-US"));
        }

        private static void AssertAppCultureUnchanged(string when)
        {
            Assert.That(CultureInfo.CurrentCulture.Name, Is.EqualTo("en-US"), "CurrentCulture " + when);
            Assert.That(CultureInfo.CurrentUICulture.Name, Is.EqualTo("en-US"), "CurrentUICulture " + when);
            Assert.That(CultureInfo.DefaultThreadCurrentCulture?.Name, Is.EqualTo("en-US"), "DefaultThreadCurrentCulture " + when);
            Assert.That(CultureInfo.DefaultThreadCurrentUICulture?.Name, Is.EqualTo("en-US"), "DefaultThreadCurrentUICulture " + when);
            Assert.That(float.Parse("1.5"), Is.EqualTo(1.5f), "float.Parse under the current culture " + when);
        }

        [Test]
        public void StartupInTurkishLeavesTheAppCultureUnchanged()
        {
            var catalog = CreateCatalog();
            var controller = new UnityLocalizationController(catalog, new MemoryPreferenceStore("tr-TR"));

            Assert.That(controller.ActivateStartupLanguage().Code, Is.EqualTo("tr-TR"));

            AssertAppCultureUnchanged("after startup in tr-TR");
            Assert.That(catalog.Culture.Name, Is.EqualTo("tr-TR"), "the catalog's culture follows the language");
        }

        [Test]
        public void SwitchToTurkishLeavesTheAppCultureUnchanged()
        {
            var catalog = CreateCatalog();
            var controller = new UnityLocalizationController(catalog, new MemoryPreferenceStore());

            controller.SwitchLanguage(new LanguageKey("tr-TR"));

            Assert.That(controller.ActiveLanguage.Code, Is.EqualTo("tr-TR"));
            AssertAppCultureUnchanged("after switching to tr-TR");
            Assert.That(catalog.Culture.Name, Is.EqualTo("tr-TR"), "the catalog's culture follows the language");
        }

        [Test]
        public void UnknownCultureCodeKeepsTheCatalogCulture()
        {
            var catalog = CreateCatalog();
            var controller = new UnityLocalizationController(catalog, new MemoryPreferenceStore());
            controller.SwitchLanguage(new LanguageKey("tr-TR"));

            Assert.DoesNotThrow(() => controller.SwitchLanguage(new LanguageKey("pseudo")));

            Assert.That(controller.ActiveLanguage.Code, Is.EqualTo("pseudo"));
            Assert.That(catalog.Get("hello"), Is.EqualTo("[Hello]"));
            Assert.That(catalog.Culture.Name, Is.EqualTo("tr-TR"), "the catalog keeps the culture it had");
            AssertAppCultureUnchanged("after switching to a code that is not a culture");
        }
    }

    /// <summary>
    /// Equivalence with the earlier behaviour, where the switch set the app culture: with tr-TR active the
    /// catalog renders exactly what it rendered then. The expected strings come from tr-TR's own formatting
    /// data, and the app culture is en-US, which formats differently, so anything still read from the app
    /// culture would show. Uses only API that existed before the change, so it also runs on the old code.
    /// </summary>
    [TestFixture]
    public sealed class TurkishRenderingEquivalenceTests : CultureSandbox
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private const double Stamp = 1700000000.5;

        private static LocalizationCatalog CreateCatalog()
        {
            var en = new Dictionary<string, string>
            {
                ["plain"] = "Value {v}", ["money"] = "Price {p}", ["date"] = "On {d}", ["stamp"] = "Stamp {s}",
                ["pos"] = "At {c}", ["posraw"] = "At {c}", ["parsed"] = "Total {t}", ["span"] = "Left {d}",
                ["dyn_1.5"] = "Dynamic en",
            };
            var tr = new Dictionary<string, string>(en) { ["dyn_1,5"] = "Dynamic tr" };
            tr.Remove("dyn_1.5");

            var defs = new LocalizationDefinitions();
            defs.Parameters["plain"] = ParameterDefinition.Parse("Num-v");                    // value's own text
            defs.Parameters["money"] = ParameterDefinition.Parse("Num-p-CurrencyDecimal");    // N2
            defs.Parameters["date"] = ParameterDefinition.Parse("Time-d-DateShort");
            defs.Parameters["stamp"] = ParameterDefinition.Parse("Time-s");                   // value's own text
            defs.Parameters["pos"] = ParameterDefinition.Parse("Vec-c-Coordinates");
            defs.Parameters["posraw"] = ParameterDefinition.Parse("Vec-c");                   // value's own text
            defs.Parameters["parsed"] = ParameterDefinition.Parse("Num-t-CurrencyDecimal");   // text read as a number
            defs.Parameters["span"] = ParameterDefinition.Parse("Dur-d-Duration");
            defs.DynamicKeys["dyn_{id}"] = ParameterDefinition.Parse("Num-id");

            var source = new InMemoryLocalizationSource()
                .Add(new LanguageKey("en-US"), en)
                .Add(new LanguageKey("tr-TR"), tr);
            return new LocalizationCatalog(source, new LanguageKey("en-US"), defs);
        }

        [Test]
        public void TurkishCatalogRendersAsWhenTheAppCultureFollowedTheLanguage()
        {
            Assume.That(1.5.ToString(Tr), Is.Not.EqualTo(1.5.ToString(En)), "tr-TR and en-US must format 1.5 differently");
            var catalog = CreateCatalog();
            var controller = new UnityLocalizationController(catalog, new MemoryPreferenceStore());

            controller.SwitchLanguage(new LanguageKey("tr-TR"));

            Assert.That(catalog.Get("plain", new NumberValue(1.5)), Is.EqualTo("Value " + 1.5.ToString(Tr)), "unformatted number");
            Assert.That(catalog.Get("money", new NumberValue(1234.5)), Is.EqualTo("Price " + 1234.5.ToString("N2", Tr)), "currency N2");
            Assert.That(catalog.Get("date", new TimestampValue(Stamp)),
                Is.EqualTo("On " + Epoch.AddSeconds(Stamp).ToLocalTime().ToString("d", Tr)), "short date");
            Assert.That(catalog.Get("stamp", new TimestampValue(Stamp)), Is.EqualTo("Stamp " + Stamp.ToString(Tr)), "unformatted timestamp");
            Assert.That(catalog.Get("pos", new CoordinatesValue(3.5, 7.25)),
                Is.EqualTo("At X:" + 3.5.ToString(Tr) + " Y:" + 7.25.ToString(Tr)), "coordinates");
            Assert.That(catalog.Get("posraw", new CoordinatesValue(3.5, 7.25)),
                Is.EqualTo("At " + 3.5.ToString(Tr) + "," + 7.25.ToString(Tr)), "unformatted coordinates");
            Assert.That(catalog.Get("parsed", new TextValue("1,5")), Is.EqualTo("Total " + 1.5.ToString("N2", Tr)), "text parsed as a number");
            Assert.That(catalog.Get("span", new DurationValue(new TimeSpan(0, 5, 6, 7))), Is.EqualTo("Left 05:06:07"), "duration");
            Assert.That(catalog.Get("dyn_{id}", new NumberValue(1.5)), Is.EqualTo("Dynamic tr"), "dynamic-key parameter");
        }
    }
}
