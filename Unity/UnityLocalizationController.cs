using System;
using System.Globalization;
using UnityEngine;

namespace PFound.LocalizationService.Unity
{
    /// <summary>
    /// Engine-facing driver around a <see cref="LocalizationCatalog"/>. It wires the core logging seam to
    /// the Unity console, decides the startup language from the persisted preference and the device
    /// culture (engine-free logic in <see cref="LanguageSelector"/>), and, on switch, persists the choice
    /// and sets the thread's current/UI culture so .NET formatting matches the chosen language.
    /// </summary>
    public sealed class UnityLocalizationController
    {
        private readonly LocalizationCatalog _catalog;
        private readonly ILanguagePreferenceStore _preferences;
        private readonly LanguageRedirections _redirections;
        private readonly LanguageKey _defaultLanguage = new LanguageKey(LocalizationConstants.DefaultLanguageCode);

        public LocalizationCatalog Catalog => _catalog;
        public LanguageKey ActiveLanguage => _catalog.ActiveLanguage;

        public UnityLocalizationController(LocalizationCatalog catalog, ILanguagePreferenceStore preferences = null)
        {
            _catalog = catalog;
            _preferences = preferences ?? new PlayerPrefsLanguageStore();
            _redirections = catalog.Definitions.Redirections ?? LanguageRedirections.CreateDefault();
            WireLogging();
        }

        /// <summary>
        /// Same as the two-argument constructor, plus the language to start in when neither the stored
        /// preference nor the device culture resolves to a supported language (used instead of
        /// <see cref="LocalizationConstants.DefaultLanguageCode"/>). Needed when the tables have no
        /// <c>en-US</c>; the catalog's fallback language is the natural choice.
        /// </summary>
        /// <exception cref="ArgumentException">The tables do not contain <paramref name="defaultLanguage"/>.</exception>
        public UnityLocalizationController(LocalizationCatalog catalog, ILanguagePreferenceStore preferences, LanguageKey defaultLanguage)
            : this(RequireSupported(catalog, defaultLanguage), preferences)
        {
            _defaultLanguage = defaultLanguage;
        }

        // Runs before the main constructor, so a bad default fails before the log sinks are rewired.
        private static LocalizationCatalog RequireSupported(LocalizationCatalog catalog, LanguageKey defaultLanguage)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (!catalog.IsLanguageSupported(defaultLanguage))
                throw new ArgumentException("Default language not available: " + defaultLanguage +
                    " (tables have: " + string.Join(", ", catalog.SupportedLanguages) + ")", nameof(defaultLanguage));
            return catalog;
        }

        private static void WireLogging()
        {
            LocalizationLog.OnWarning = Debug.LogWarning;
            LocalizationLog.OnError = Debug.LogError;
        }

        /// <summary>
        /// Chooses and activates the startup language: a supported persisted preference, else the device
        /// culture (direct or redirected), else the default language (the one given to the constructor,
        /// otherwise <see cref="LocalizationConstants.DefaultLanguageCode"/>). Clears a stale preference.
        /// Also aligns the thread culture.
        /// </summary>
        public LanguageKey ActivateStartupLanguage()
        {
            _preferences.TryGet(out string preferred);
            string device = DeviceCultureProvider.CurrentCultureCode;

            var chosen = LanguageSelector.Resolve(
                preferred, device, _catalog.SupportedLanguages, _redirections, _defaultLanguage, out bool clearPreference);

            if (clearPreference) _preferences.Clear();

            Apply(chosen, saveAsPreference: false);
            return chosen;
        }

        /// <summary>
        /// Switches to <paramref name="language"/> (validating support), optionally persisting it as the
        /// preference, loading its content, and setting the current/UI culture.
        /// </summary>
        public void SwitchLanguage(LanguageKey language, bool saveAsPreference = true)
        {
            if (!_catalog.IsLanguageSupported(language))
            {
                // Try to redirect an unsupported request before giving up.
                if (LanguageSelector.TryResolveCode(language.Code, _catalog.SupportedLanguages, _redirections, out var redirected))
                    language = redirected;
                else
                    return;
            }
            Apply(language, saveAsPreference);
        }

        private void Apply(LanguageKey language, bool saveAsPreference)
        {
            _catalog.SwitchLanguage(language);
            if (saveAsPreference) _preferences.Set(language.Code);
            SetThreadCulture(language.Code);
        }

        private void SetThreadCulture(string code)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(code);
                // Process-wide default for every thread spawned hereafter (background/pooled threads too),
                // then the current thread, then the catalog's formatting culture.
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                _catalog.SetCulture(culture);
            }
            catch (CultureNotFoundException)
            {
                // Non-standard code (still a valid localization key): keep the existing thread culture.
            }
        }
    }
}
