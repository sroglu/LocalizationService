# LocalizationService

## Purpose

Key→text localization with a pluggable content source, active/fallback language resolution,
parameter formatting and `{tag}` substitution, device/preference-driven startup language selection,
and a CSV/Google-Sheets editor pipeline. The core is engine-free and synchronous; a thin Unity layer
adds file loading, culture, and preference persistence. There is **no `MonoBehaviour` host** — the
consumer owns plain objects.

## Assemblies

- `PFound.LocalizationService` (runtime) — engine-free core (source contract, spine, facade,
  formatting, parameters, selection, CSV/INI parsing). `noEngineReferences: true`,
  `autoReferenced: false`.
- `PFound.LocalizationService.Unity` — engine-facing driver + file/culture/preference glue.
  References `PFound.LocalizationService`, `PFound.Compression`. `autoReferenced: false`.
- `PFound.LocalizationService.Editor` — CSV / Google-Sheets / enum authoring tools (rootNamespace
  `PFound.LocalizationService.EditorTools`). References the runtime, `PFound.Compression`,
  `PFound.Utilities.EditorHelpers`.
- `PFound.LocalizationService.Tests` — NUnit suite (mono-testable core).

## Dependencies

- `PFound.Compression` — the Unity layer inflates `.lzma` tables via the shared codec.
- `PFound.Utilities.EditorHelpers` — editor tooling only (`ImportableAsset`/`ImportableAssetEditor`
  for the Google-Sheets config).
- No scripting defines. The core has no engine or third-party dependency.

## Key Types

**Core (`PFound.LocalizationService`)**

- `ILocalizationSource` — supplies the key→text table per language. Implementations:
  `InMemoryLocalizationSource` (code/tests), `DirectoryLocalizationSource` (folder of INI text files;
  definitions eager, content lazy). `ContentTables` backs the in-memory tables.
- `LocalizationService` — the spine: owns active/fallback tables + cache and language switching.
- `LocalizationCatalog` — the facade: layers parameter definitions, dynamic (template) keys, `{tag}`
  substitution, and the `FormatterRegistry` on top of the spine. Implements `ILocalizationContext`.
- `LanguageKey` (case-insensitive language code, e.g. `"en"`), `LocalizationKey` (implicit from
  `string`).
- `LocalizationDefinitions`, `ParameterDefinition` / `ParameterSpec`, `LanguageInfo`,
  `LanguageRedirections`, `LanguageSelector` (static device/preference resolution).
- `FormatterRegistry` + `IValueFormatter` (`BuiltInFormatters`, `NumberAbbreviator`),
  `ILocalizationValue` + `LocalizationValues`, `IValueKeyResolver`.
- INI/CSV: `IniDocument`, `CsvReader`, `LocalizationTableBuilder`. `LocalizableEnumAttribute`
  (`[LocalizableEnum]`), `EnumKeyGenerator`. `LocalizationLog` (pluggable log sink).

**Unity (`PFound.LocalizationService.Unity`)**

- `UnityLocalizationController` — the engine-facing driver.
- `TableFileLoader` — reads tables from StreamingAssets / persistentDataPath, `.lzma`-aware.
- `DeviceCultureProvider`, `ILanguagePreferenceStore` (default `PlayerPrefsLanguageStore`),
  `LocalizationHashData`.

**Editor (`PFound.LocalizationService.EditorTools`)** — `CsvTableConverter`,
`LocalizableEnumScanner`, `GoogleSheetsConfig` + `GoogleSheetsConfigEditor`.

## Public API

**Lookup (`LocalizationCatalog`)**

- `string Get(LocalizationKey key, params ILocalizationValue[] values)`.
- `bool TryGet(key, out string text, params ...)` and the fallback-aware overload
  `bool TryGet(key, out string text, out bool fromFallback, params ...)`.
- `string GetEnsured(key, ...)` — throws when missing.
- `string GetOrErrorText(key, ...)` — logs + returns a visible `# [key] #` placeholder.
- Config seams: `SetValueKeyResolver(IValueKeyResolver)`, `SetCulture(CultureInfo)`,
  `FormatterRegistry Formatters`, `LocalizationDefinitions Definitions`, `CultureInfo Culture`.
- Language passthrough: `LanguageKey ActiveLanguage`, `IReadOnlyList<LanguageKey> SupportedLanguages`,
  `bool IsLanguageSupported(LanguageKey)`, `void SwitchLanguage(LanguageKey)`.
- Constructor: `new LocalizationCatalog(ILocalizationSource content, LanguageKey fallbackLanguage,
  LocalizationDefinitions definitions = null, FormatterRegistry formatters = null, CultureInfo culture
  = null)`.

**Driver (`UnityLocalizationController`)**

- `new UnityLocalizationController(LocalizationCatalog catalog, ILanguagePreferenceStore preferences =
  null)` — default store is `PlayerPrefsLanguageStore`.
- `LanguageKey ActivateStartupLanguage()` — pick + apply the startup language once at boot.
- `void SwitchLanguage(LanguageKey language, bool saveAsPreference = true)` — persists + reloads + sets
  thread culture.
- `LocalizationCatalog Catalog`, `LanguageKey ActiveLanguage`.

**Loading (`TableFileLoader`, static)** — `StreamingAssetsTablesDir`, `PersistentTablesDir`,
`ReadTable(path)`, `(LocalizationDefinitions definitions, InMemoryLocalizationSource content)
LoadFrom(string directory)`.

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

Rules:

- Call `ActivateStartupLanguage()` exactly once during startup, before any UI reads text. It picks
  the startup language from the persisted preference + device culture (via `LanguageSelector`).
- The `fallbackLanguage` passed to `LocalizationCatalog` **must exist** in the source — it is loaded
  eagerly and throws if absent.
- Main-thread only.
- To swap persistence, pass your own `ILanguagePreferenceStore` to the controller.

## File Structure

```
LocalizationService/
  Runtime/                                # engine-free core (PFound.LocalizationService)
    ILocalizationSource.cs, LocalizationService.cs, LocalizationKeys.cs, LocalizationConstants.cs
    Sources/       ContentTables, DirectoryLocalizationSource, InMemoryLocalizationSource
    Facade/        LocalizationCatalog, IValueKeyResolver
    Formatting/    FormatterRegistry, BuiltInFormatters, NumberAbbreviator, IValueFormatter, ILocalizationContext
    Parameters/    ParameterDefinition, ParameterSpec, LocalizationValues, ILocalizationValue,
                   LocalizationFormat, LocalizableEnumAttribute, EnumKeyGenerator
    Model/         LanguageInfo, LocalizationDefinitions
    Selection/     LanguageSelector, LanguageRedirections
    Csv/           CsvReader, LocalizationTableBuilder
    Ini/           IniDocument
    Tags/          TagScanner
    Diagnostics/   LocalizationLog
  Unity/                                  # PFound.LocalizationService.Unity
    UnityLocalizationController, TableFileLoader(.Hashes), DeviceCultureProvider,
    ILanguagePreferenceStore, PlayerPrefsLanguageStore, LocalizationHashData
  Editor/                                 # PFound.LocalizationService.Editor
    CsvTableConverter, LocalizableEnumScanner, GoogleSheets/{GoogleSheetsConfig,GoogleSheetsConfigEditor}
  Tests/                                  # LocalizationServiceTests, LocalizationV2Tests
```

## Downstream Dependents

None within PFound. Consumed directly by game bootstrap/UI code that references the runtime (and
`.Unity`) assembly.

## Extension points

- **Custom content source** — implement `ILocalizationSource` to feed the catalog from any backend
  (remote table, addressables, encrypted blob). The spine only needs per-language key→text tables.
- **Custom preference store** — implement `ILanguagePreferenceStore` and pass it to the controller to
  persist the language choice somewhere other than `PlayerPrefs`.
- **Custom formatters** — register `IValueFormatter` instances in the `FormatterRegistry` for typed
  parameter formatting (numbers, dates, abbreviations).
- **Value-key resolution** — supply an `IValueKeyResolver` via `SetValueKeyResolver` to map parameter
  values to nested localization keys (e.g. enum → localized label).

## Editor pipeline

`Editor/` provides the authoring side: `CsvTableConverter`, a Google-Sheets config + pull
(`GoogleSheets/`), and `LocalizableEnumScanner` (`[LocalizableEnum]` → generated keys). These emit the
INI text tables the runtime source consumes.

## Limitations / Known Gaps

- Main-thread, synchronous only — no async table streaming in the core (the Unity loader reads files
  eagerly; wrap it yourself for async load).
- The fallback language is eager and mandatory; a missing fallback table is a hard failure by design.
- No live/hot reload of tables at runtime beyond an explicit `SwitchLanguage`; edit-time regeneration
  is the editor pipeline's job.
- `{tag}` substitution and parameter formatting run per lookup — cache results for text drawn every
  frame.

## Testing

The core is engine-free — drive it with `InMemoryLocalizationSource` and assert
lookup/fallback/tag/formatting behavior (`LocalizationServiceTests`, `LocalizationV2Tests`) under
`csc`/`mono` or Unity EditMode. The Unity file/culture layer is verified in the editor.
