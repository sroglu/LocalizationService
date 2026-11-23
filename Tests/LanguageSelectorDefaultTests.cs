using System.Collections.Generic;
using NUnit.Framework;

namespace PFound.LocalizationService.Tests
{
    /// <summary>
    /// The <see cref="LanguageSelector"/> overload that takes a default language, over tables with
    /// <c>en-GB</c> and <c>tr-TR</c> but no <c>en-US</c>: the given default replaces the built-in
    /// <c>en-US</c>, and only as the last resort.
    /// </summary>
    [TestFixture]
    public sealed class LanguageSelectorDefaultTests
    {
        private static readonly LanguageKey EnGb = new LanguageKey("en-GB");

        private static LanguageKey Resolve(string preferred, string deviceCulture, out bool clearPreference)
        {
            var supported = new List<LanguageKey> { new LanguageKey("en-GB"), new LanguageKey("tr-TR") };
            return LanguageSelector.Resolve(preferred, deviceCulture, supported, LanguageRedirections.CreateDefault(), EnGb, out clearPreference);
        }

        [Test]
        public void UnresolvableDeviceCultureGivesTheDefault()
        {
            Assert.That(Resolve(null, "fr-FR", out bool clear).Code, Is.EqualTo("en-GB"));
            Assert.That(clear, Is.False);
        }

        [Test]
        public void DeviceCultureRedirectedToMissingEnUsGivesTheDefault()
        {
            // en-AU matches the built-in en-* -> en-US rule, and the tables have no en-US.
            Assert.That(Resolve(null, "en-AU", out bool clear).Code, Is.EqualTo("en-GB"));
            Assert.That(clear, Is.False);
        }

        [Test]
        public void StalePreferenceIsClearedAndTheDefaultGiven()
        {
            Assert.That(Resolve("de-DE", "fr-FR", out bool clear).Code, Is.EqualTo("en-GB"));
            Assert.That(clear, Is.True);
        }

        [Test]
        public void SupportedPreferenceAndDeviceCultureStillWinOverTheDefault()
        {
            Assert.That(Resolve("tr-TR", "fr-FR", out _).Code, Is.EqualTo("tr-TR"), "preference");
            Assert.That(Resolve(null, "tr-TR", out _).Code, Is.EqualTo("tr-TR"), "device culture");
        }
    }

    /// <summary>
    /// The original <see cref="LanguageSelector"/> overload, which takes no default, keeps every outcome
    /// it had before the default became configurable: its last resort is still
    /// <see cref="LocalizationConstants.DefaultLanguageCode"/>.
    /// </summary>
    [TestFixture]
    public sealed class LanguageSelectorRegressionTests
    {
        [TestCase("tr-TR", "en-US", "tr-TR", false)] // supported preference wins
        [TestCase("en-UK", "tr-TR", "en-GB", false)] // preference redirected (exact rule)
        [TestCase("en-AU", "tr-TR", "en-US", false)] // preference redirected (en-* rule)
        [TestCase("de-DE", "tr-TR", "tr-TR", true)]  // stale preference, then the device culture
        [TestCase("de-DE", "en-AU", "en-US", true)]  // stale preference, then the redirected device culture
        [TestCase("de-DE", "fr-FR", "en-US", true)]  // stale preference, then the last resort
        [TestCase(null, "en-GB", "en-GB", false)]    // device culture supported directly
        [TestCase(null, "en-UK", "en-GB", false)]    // device culture redirected (exact rule)
        [TestCase(null, "en-AU", "en-US", false)]    // device culture redirected (en-* rule)
        [TestCase(null, "fr-FR", "en-US", false)]    // last resort
        [TestCase(null, "", "en-US", false)]         // invariant device culture
        [TestCase("", "tr-TR", "tr-TR", false)]      // an empty preference counts as none
        public void OutcomeIsUnchanged(string preferred, string deviceCulture, string expected, bool expectedClear)
        {
            var supported = new List<LanguageKey> { new LanguageKey("en-US"), new LanguageKey("en-GB"), new LanguageKey("tr-TR") };

            var chosen = LanguageSelector.Resolve(preferred, deviceCulture, supported, LanguageRedirections.CreateDefault(), out bool clear);

            Assert.That(chosen.Code, Is.EqualTo(expected));
            Assert.That(clear, Is.EqualTo(expectedClear));
        }

        [Test]
        public void WithoutEnUsInTheTablesTheLastResortIsStillEnUs()
        {
            var supported = new List<LanguageKey> { new LanguageKey("en-GB"), new LanguageKey("tr-TR") };

            var chosen = LanguageSelector.Resolve(null, "fr-FR", supported, LanguageRedirections.CreateDefault(), out _);

            Assert.That(chosen.Code, Is.EqualTo(LocalizationConstants.DefaultLanguageCode));
        }
    }
}
