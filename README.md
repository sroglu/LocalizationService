# PFound.LocalizationService

Key→text localization: a pluggable content source, active/fallback language resolution, parameter
formatting and `{tag}` substitution, device/preference-driven startup language selection, and a
CSV/Google-Sheets editor pipeline. The core is engine-free and synchronous; a thin Unity layer adds
file loading, culture, and preference persistence. There is **no `MonoBehaviour` host** — you own
plain objects.

## Model

- **`ILocalizationSource`** supplies the key→text table per language. Ships:
  `InMemoryLocalizationSource` (code/tests), `DirectoryLocalizationSource` (folder of INI text
  files, definitions eager + content lazy). The Unity `TableFileLoader` reads from
  StreamingAssets / persistentDataPath and inflates `.lzma` tables via the shared `PFound.Compression`
  codec.
- **`LocalizationService`** (spine) — owns the active/fallback tables + cache and language switching;
  `Get(key, args)` resolves active → fallback → the raw key string, with `string.Format`.
- **`LocalizationCatalog`** (facade) — layers parameter definitions, dynamic (template) keys, `{tag}`
  substitution, and the `FormatterRegistry` on top of the spine.
- **`UnityLocalizationController`** — engine-facing driver: routes core logging to the Unity console,
  picks the startup language from the persisted preference + device culture (via `LanguageSelector`),
  and on switch persists the choice and aligns the thread `CurrentCulture`/`CurrentUICulture`.

## Public API

**Lookup (`LocalizationCatalog`)**: `Get(key, …values)`, `TryGet(key, out text, …)` (and a
fallback-aware overload with `out fromFallback`), `GetEnsured(key, …)` (throws when missing),
`GetOrErrorText(key, …)` (logs + visible `# [key] #` placeholder). Config seams:
`SetValueKeyResolver`, `SetCulture`, `Formatters`, `Definitions`. Language passthrough:
`ActiveLanguage`, `SupportedLanguages`, `IsLanguageSupported`, `SwitchLanguage`.

**Driver (`UnityLocalizationController`)**: `ActivateStartupLanguage()`,
`SwitchLanguage(language, saveAsPreference = true)`, `Catalog`, `ActiveLanguage`.

**Keys**: `LanguageKey` (case-insensitive code, e.g. `"en"`), `LocalizationKey` (implicit from
`string`). Preference persistence: `ILanguagePreferenceStore` (default `PlayerPrefsLanguageStore`).

## Setup / wiring

Plain library — no scene object, no singleton. Build the objects once at boot, keep the
`UnityLocalizationController` (or its `Catalog`) reference, and share it (e.g. register it in
`DependencyContainer`). The consumer, not the module, decides where it lives and how long it lasts.

```csharp
// 1. content source — folder of tables under StreamingAssets, .lzma-aware
var (definitions, content) = TableFileLoader.LoadFrom(TableFileLoader.StreamingAssetsTablesDir);
// (or DirectoryLocalizationSource for plain folders, or InMemoryLocalizationSource for code tables)

// 2. facade over the source, with a guaranteed fallback language
var catalog = new LocalizationCatalog(content, new LanguageKey("en"), definitions);

// 3. Unity driver: wires logging + persistence + thread culture
var controller = new UnityLocalizationController(catalog);   // default PlayerPrefs store
controller.ActivateStartupLanguage();                        // pick + apply startup language ONCE at boot

// use it:
string title = catalog.Get("menu.title");
controller.SwitchLanguage(new LanguageKey("tr"));            // persists + reloads + sets culture
```

Call `ActivateStartupLanguage()` exactly once during startup, before any UI reads text. The fallback
language passed to `LocalizationCatalog` must exist in the source (it is loaded eagerly and throws if
absent). Main-thread only. To swap persistence, pass your own `ILanguagePreferenceStore` to the
controller.

## Editor pipeline

`Editor/` provides the authoring side: `CsvTableConverter`, a Google-Sheets config + pull
(`GoogleSheets/`), and `LocalizableEnumScanner` (`[LocalizableEnum]` → generated keys). These emit
the INI text tables the runtime source consumes.

## Layout

- `Runtime/` — engine-free core: source contract + sources, spine + facade, formatting, parameters,
  selection, CSV/INI parsing. Assembly `PFound.LocalizationService` (`noEngineReferences`).
- `Unity/` — `UnityLocalizationController`, `TableFileLoader`, `DeviceCultureProvider`,
  `PlayerPrefsLanguageStore`. Assembly `PFound.LocalizationService.Unity` (references
  `PFound.Compression` for `.lzma` tables).
- `Editor/` — CSV/Sheets/enum authoring tools. `Tests/` — NUnit suite (mono-testable core).

## Testing

The core is engine-free — drive it with `InMemoryLocalizationSource` and assert
lookup/fallback/tag/formatting behavior under `csc`/`mono` or Unity EditMode. The Unity file/culture
layer is verified in the editor.
