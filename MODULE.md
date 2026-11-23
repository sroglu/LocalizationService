# LocalizationService

> **Module group — Localization.** Single-module group. Grouped by purpose — see the catalog `Assets/PFound/README.md` and each module's **Dependencies** for exact edges.

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
- `PFound.LocalizationService.BestHttp` — remote/CDN table acquisition over BestHTTP (rootNamespace
  `PFound.LocalizationService.Unity`). References `PFound.LocalizationService`,
  `PFound.LocalizationService.Unity`, `BestHTTP`. Gated by the `PFOUND_BESTHTTP` define constraint,
  `autoReferenced: false` — the module compiles without the library present.
- `PFound.LocalizationService.Editor` — CSV / Google-Sheets / enum authoring tools (rootNamespace
  `PFound.LocalizationService.EditorTools`). References the runtime, `PFound.Compression`,
  `PFound.Utilities.EditorHelpers`.
- `PFound.LocalizationService.Tests` (engine-free core, `noEngineReferences: true`) and
  `PFound.LocalizationService.Unity.Tests` (Unity layer: loader, controller) — NUnit EditMode tests.
  Both are editor-only and compile only under `UNITY_INCLUDE_TESTS`, so they never reach a player build.

## Dependencies

- `PFound.Compression` — the Unity layer inflates `.lzma` tables via the shared codec.
- `PFound.Utilities.EditorHelpers` — editor tooling only (`ImportableAsset`/`ImportableAssetEditor`
  for the Google-Sheets config).
- `BestHTTP` — third-party HTTP transport, used **only** by the `PFound.LocalizationService.BestHttp`
  assembly for remote/CDN table acquisition; gated behind the `PFOUND_BESTHTTP` define constraint.
- Scripting defines: `PFOUND_BESTHTTP` (compiles the BestHTTP remote-acquisition assembly),
  `PFOUND_LOCALIZATION_QA` (compiles the non-shipping QA diagnostics hook on the catalog). See
  `## Conditional Compilation`. The engine-free core itself has no engine or third-party dependency.

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
- `LocalizationKeyReference` / `LanguageKeyReference` — `[Serializable]` authoring counterparts of the
  readonly-struct runtime keys (serialized field `Key`); convert to the runtime key via `ToKey()` or
  the implicit operator. Used by inspector/ScriptableObject content that authors keys.
- `LocalizationDefinitions`, `ParameterDefinition` / `ParameterSpec`, `LanguageInfo`,
  `LanguageRedirections`, `LanguageSelector` (static device/preference resolution).
- `FormatterRegistry` + `IValueFormatter` (`BuiltInFormatters`, `NumberAbbreviator`),
  `ILocalizationValue` + `LocalizationValues`, `IValueKeyResolver`. `BuiltInFormatters` renders whole
  days in the `Duration` format as the count + a localized day-unit label (key
  `LocalizationConstants.DayAbbreviationKey`).
- `LocalizationDiagnostics` — build-gated QA layer (show-keys + pseudo-localization), engine-free and
  deterministic. Surfaced on the catalog only under `PFOUND_LOCALIZATION_QA`.
- INI/CSV: `IniDocument` (validates on `Write()` — see below), `CsvReader`, `LocalizationTableBuilder`.
  `LocalizableEnumAttribute` (`[LocalizableEnum]`), `EnumKeyGenerator`. `LocalizationLog`
  (pluggable log sink).

**Unity (`PFound.LocalizationService.Unity`)**

- `UnityLocalizationController` — the engine-facing driver. `SwitchLanguage` sets the catalog's
  culture only; the app and thread cultures keep their values (see **Culture** below).
- `TableFileLoader` — reads tables from StreamingAssets / persistentDataPath, `.lzma`-aware.
  `LoadFromStreamingAssetsAsync` reads StreamingAssets on every platform, Android included (see
  **Loading tables on every platform** below). The `.Hashes` partial adds content-address hash
  resolution + remote path/hash builders + the local-vs-remote refresh trigger.
- `DeviceCultureProvider`, `ILanguagePreferenceStore` (default `PlayerPrefsLanguageStore`),
  `LocalizationHashData` (readonly `ContentHash`/`DefinitionsHash` pair, `IsValid`, `Invalid`).

**BestHTTP (`PFound.LocalizationService.BestHttp`, `PFOUND_BESTHTTP`-gated)**

- `RemoteTableAcquisition` — downloads the hash-addressed content + definitions tables from a CDN base
  URL into the writable persistent Localizables folder (its own BestHTTP transport), skipping the
  transfer when the local hashes already match the remote manifest.

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
- `new UnityLocalizationController(LocalizationCatalog catalog, ILanguagePreferenceStore preferences,
  LanguageKey defaultLanguage)` — also sets the startup language used when neither the preference nor
  the device culture resolves (instead of `en-US`). Throws `ArgumentException` naming the code when the
  tables do not contain it.
- `LanguageKey ActivateStartupLanguage()` — pick + apply the startup language once at boot.
- `void SwitchLanguage(LanguageKey language, bool saveAsPreference = true)` — persists + reloads + sets
  the catalog's culture.
- `LocalizationCatalog Catalog`, `LanguageKey ActiveLanguage`.

**Startup selection (`LanguageSelector`, static)** — `Resolve(preferred, deviceCulture, supported,
redirections, out bool clearPreference)` ends with `en-US`; the overload with a `LanguageKey
defaultLanguage` before the `out` parameter ends with that language instead.

**Loading (`TableFileLoader`, static)** — `StreamingAssetsTablesDir`, `PersistentTablesDir`,
`ReadTable(path)`, `(LocalizationDefinitions definitions, InMemoryLocalizationSource content)
LoadFrom(string directory)`, `Task<(LocalizationDefinitions definitions, InMemoryLocalizationSource
content)> LoadFromStreamingAssetsAsync()`, `const string TableIndexFileName` (`"index.txt"`).

**Hash / remote paths (`TableFileLoader.Hashes`, static)**

- `string GetFileHashFromFileName(string fileName)`, `string GetHashFromUrl(string url)`.
- `string PersistentContentFilePath(string hash)`, `string PersistentDefinitionsFilePath(string hash)`.
- `string RemoteContentPath(string hash)`, `string RemoteDefinitionsPath(string hash)`.
- `bool NeedsRefresh(LocalizationHashData local, LocalizationHashData remote)` — the update trigger.
- `void ClearPersistentTablesDir()`, `LocalizationHashData GetLocalFileHashes()` /
  `GetLocalFileHashes(string directory)`.

**Remote acquisition (`RemoteTableAcquisition`, `PFOUND_BESTHTTP`)**

- `new RemoteTableAcquisition(string baseUrl)` — CDN base URL the hash-addressed paths compose against.
- `Task<bool> RefreshAsync(LocalizationHashData remote, CancellationToken cancellationToken = default)`
  — false when the local set is already current; true after downloading a fresh set.

**QA diagnostics (`LocalizationDiagnostics`, surfaced under `PFOUND_LOCALIZATION_QA`)**

- `bool ShowKeys`, `bool PseudoLocalize`, `float ExpansionFactor` (clamped non-negative).
- `string Apply(LocalizationKey key, string localized)`, `string Pseudoize(string text)`.
- On the catalog: `LocalizationDiagnostics Diagnostics { get; }` (compiled in only under the define).

**Formatter registry (`FormatterRegistry`)** — `void Register(IValueFormatter)`,
`bool Unregister(IValueFormatter)` (false if not registered), `void Clear()`.

**Authoring keys** — `LocalizationKeyReference` / `LanguageKeyReference`: `Key` field, `bool IsValid`,
`LocalizationKey ToKey()` / `LanguageKey ToKey()`, implicit conversions to/from `string` and the
runtime key.

## Setup / wiring

Plain library — no scene object, no singleton. Build the objects once at boot, keep the
`UnityLocalizationController` (or its `Catalog`) reference, and share it (e.g. register it in
`DependencyContainer`). The consumer, not the module, decides where it lives and how long it lasts.

The example runs in an async method on the main thread (for example an `async void Start()`), with
tables that have `[en-US]` and `[tr-TR]` sections in `StreamingAssets/Localizables/`:

```csharp
// 1. content source: the tables in StreamingAssets/Localizables, read on every platform
var (definitions, content) = await TableFileLoader.LoadFromStreamingAssetsAsync();

// 2. facade over the source; the fallback language must be in the tables
var fallback = new LanguageKey("en-US");
var catalog = new LocalizationCatalog(content, fallback, definitions);

// 3. Unity driver: logging + persistence + the catalog's culture; the fallback is also the startup default
var controller = new UnityLocalizationController(catalog, null, fallback);   // null: default PlayerPrefs store
controller.ActivateStartupLanguage();                                        // pick + apply the startup language ONCE at boot

// use it:
string title = catalog.Get("menu.title");
controller.SwitchLanguage(new LanguageKey("tr-TR"));                         // persists + reloads + sets the catalog's culture
```

Other sources work the same way: `TableFileLoader.LoadFrom(directory)` for a folder (synchronous),
`DirectoryLocalizationSource` for plain folders, `InMemoryLocalizationSource` for code tables.

Rules:

- Call `ActivateStartupLanguage()` exactly once during startup, before any UI reads text. It picks
  the startup language from the persisted preference + device culture (via `LanguageSelector`).
- The `fallbackLanguage` passed to `LocalizationCatalog` **must exist** in the source — it is loaded
  eagerly and throws if absent. That includes a load that found no tables at all: the catalog
  constructor throws `Fallback language not available`.
- Pass the startup default (the controller's third argument) when the tables have no `en-US`. Without
  it the last resort is `en-US`, and startup throws when the tables lack it; with it nothing else
  changes. A default that is not in the tables throws `ArgumentException` naming the code.
- Main-thread only.
- To swap persistence, pass your own `ILanguagePreferenceStore` to the controller.

**Loading tables on every platform.** `LoadFromStreamingAssetsAsync` reads `StreamingAssetsTablesDir`
with file reads where StreamingAssets is a folder (Editor, desktop, iOS) and with `UnityWebRequest`
where it is a URL (Android, where it lives inside the APK, and WebGL). It reads the tables into memory
(no copy step) with the same rules as `LoadFrom`: plain `.txt` before `.lzma`, exact names before
suffixed ones. Call it from the main thread and await it; the requests are created and awaited there.
Never block on it with `.Result`, `.Wait()` or `GetAwaiter().GetResult()`: on Android and WebGL the
task completes in a request callback on the main thread, so blocking that thread hangs the boot. In the
Editor, on desktop and on iOS the task is already complete when returned, so a blocking call there hides
the problem.

- Exact names (`LocalizationDefinitions.txt`, `LocalizationText.lzma`, ...) need no extra file.
- Suffixed names (`LocalizationText-<hash>.lzma`) need the optional index file `index.txt`
  (`TableIndexFileName`) where the folder cannot be listed (Android, WebGL). Format: UTF-8 (a leading
  byte order mark is allowed), LF or CRLF, one file name per line relative to the folder; whitespace is
  trimmed, and blank lines, `#` comments and lines with `/` or `\` are skipped. Per table the order is:
  exact `.txt`, listed `.txt` names in index order, exact `.lzma`, listed `.lzma` names; a listed name
  that is missing is skipped. Where an index exists it is used on every platform, so all of them pick
  the same files; without one, a folder is listed as `LoadFrom` does.
- A missing table gives an empty part plus a `[Localization]` warning; over a URL the warning also names
  the last failed request with its error and response code. A missing folder gives one warning instead
  where it can be checked (not over a URL). Nothing throws. A catalog built over empty tables throws, as
  noted in the rules above.

**Culture.** Switching the language no longer changes the app's culture. Before, the controller set
`CultureInfo.CurrentCulture`, `CurrentUICulture` and the defaults for new threads to the chosen language,
so all code in the app parsed and formatted like that language (under tr-TR, `float.Parse("1.5")`
returned 15). Now only the catalog follows the language: it formats and parses parameter values under
`catalog.Culture`, so localized strings look exactly as before. Code outside the catalog keeps the
culture the app started with; if it formats numbers or dates for the player, it should use
`catalog.Culture`. The same applies to custom `IValueFormatter` and `IValueKeyResolver` code that formats
or parses by itself: it should use `context.Culture`. The built-in values' own `AppendTo` and
`TryGetDouble` already follow the catalog's culture while the catalog calls them. A catalog given its own
culture (constructor or `SetCulture`) also uses it for a value's plain text and for text read as a
number. `LocalizationService.Get(key, args)` (the spine's `string.Format` helper, not used by the
catalog) formats with the thread culture, which no longer follows the language.
`DeviceCultureProvider.CurrentCultureCode` keeps reporting the device culture after a switch.

**Remote / CDN table source (optional).** Define `PFOUND_BESTHTTP` to compile the
`PFound.LocalizationService.BestHttp` assembly, then before `LoadFrom(PersistentTablesDir)` refresh the
writable set against a manifest:

```csharp
var acquisition = new RemoteTableAcquisition(cdnBaseUrl);
await acquisition.RefreshAsync(remoteHashes);   // no-op when local hashes already match
var (definitions, content) = TableFileLoader.LoadFrom(TableFileLoader.PersistentTablesDir);
```

`RefreshAsync` clears the writable dir and pulls the hash-addressed files only when
`TableFileLoader.NeedsRefresh` sees a hash mismatch (`remoteHashes` are typically built from a manifest
via `TableFileLoader.GetHashFromUrl`). The transport lives in its own asmdef so the module compiles
without BestHTTP present.

**QA modes (non-shipping).** Define `PFOUND_LOCALIZATION_QA` to expose `catalog.Diagnostics`. Toggle
`Diagnostics.ShowKeys` (lookups return the raw key — spot missing/mis-wired keys) or
`Diagnostics.PseudoLocalize` (accented look-alikes + length expansion — spot font-coverage and layout
overflow). The gate compiles out entirely in shipping builds, so output is never altered.

**INI write-time validation.** `IniDocument.Write()` validates as it serializes (well-formed section
headers, no whitespace-padded keys/values, no reserved stored-newline escape in a value, no mixed
newline styles, no blank lines) and throws `InvalidOperationException` rather than emit a corrupt table.

**INI byte order mark.** `IniDocument.Parse` skips a leading UTF-8 byte order mark, so a table saved
with one (plain, or inside an `.lzma` file) keeps its first section.

## File Structure

```
LocalizationService/
  Runtime/                                # engine-free core (PFound.LocalizationService)
    ILocalizationSource.cs, LocalizationService.cs, LocalizationKeys.cs, LocalizationConstants.cs
    Sources/       ContentTables, DirectoryLocalizationSource, InMemoryLocalizationSource
    Facade/        LocalizationCatalog, IValueKeyResolver
    Formatting/    FormatterRegistry, BuiltInFormatters, NumberAbbreviator, IValueFormatter, ILocalizationContext,
                   ValueCulture (internal)
    Parameters/    ParameterDefinition, ParameterSpec, LocalizationValues, ILocalizationValue,
                   LocalizationFormat, LocalizableEnumAttribute, EnumKeyGenerator
    Model/         LanguageInfo, LocalizationDefinitions
    Selection/     LanguageSelector, LanguageRedirections
    Csv/           CsvReader, LocalizationTableBuilder
    Ini/           IniDocument
    Tags/          TagScanner
    Diagnostics/   LocalizationLog, LocalizationDiagnostics
  Unity/                                  # PFound.LocalizationService.Unity
    UnityLocalizationController, TableFileLoader(.Hashes, .StreamingAssets), DeviceCultureProvider,
    ILanguagePreferenceStore, PlayerPrefsLanguageStore, LocalizationHashData
  BestHttp/                               # PFound.LocalizationService.BestHttp (PFOUND_BESTHTTP)
    RemoteTableAcquisition
  Editor/                                 # PFound.LocalizationService.Editor
    CsvTableConverter, LocalizableEnumScanner, GoogleSheets/{GoogleSheetsConfig,GoogleSheetsConfigEditor}
  Tests/                                  # PFound.LocalizationService.Tests (engine-free core)
    LocalizationServiceTests, LocalizationV2Tests, ByteOrderMarkTests, LanguageSelectorDefaultTests,
    CatalogCultureTests
    Unity/                                # PFound.LocalizationService.Unity.Tests
      TableFileLoaderByteOrderMarkTests, StreamingAssetsLoadingTests, UnityLocalizationControllerTests,
      LanguageSwitchCultureTests, SetupExampleTests
```

## Downstream Dependents

None within PFound. Consumed directly by game bootstrap/UI code that references the runtime (and
`.Unity`) assembly.

## Conditional Compilation

- `PFOUND_BESTHTTP` — define constraint on the `PFound.LocalizationService.BestHttp` assembly; compiles
  `RemoteTableAcquisition` (remote/CDN table download over BestHTTP). Absent → the assembly is skipped
  and the module builds without the library. The rest of the module never references BestHTTP.
- `PFOUND_LOCALIZATION_QA` — exposes `LocalizationCatalog.Diagnostics` and its show-keys /
  pseudo-localization branches in the lookup path. Off by default; compiled out of shipping builds so
  resolved output is unchanged. Keep it a non-shipping (dev/QA) build flag.

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

- Main-thread only. The core is synchronous; the Unity layer's one async loader is
  `LoadFromStreamingAssetsAsync`, which reads whole tables into memory.
- The fallback language is eager and mandatory; a missing fallback table is a hard failure by design.
- No live/hot reload of tables at runtime beyond an explicit `SwitchLanguage`; edit-time regeneration
  is the editor pipeline's job.
- `{tag}` substitution and parameter formatting run per lookup — cache results for text drawn every
  frame.
- A table-provided prefix rule (`en-=en-GB`) cannot override the built-in `en-` → `en-US` rule: the
  built-in prefix rules are checked first. Exact rules (`en-AU=en-GB`) are checked before any prefix rule,
  so those do work. With tables that have no `en-US`, pass a startup default to the controller.

## Testing

Two NUnit EditMode assemblies (see **Assemblies**); there is no standalone `Main()` runner. Run them in
the Unity Test Runner (EditMode) or in batch mode with the project closed:

```
<Unity> -batchmode -projectPath <project> -runTests -testPlatform EditMode
  -assemblyNames "PFound.LocalizationService.Tests;PFound.LocalizationService.Unity.Tests"
  -testResults <results.xml> -logFile <editor.log>
```

Leave out `-quit`, which cuts the test run short. The core tests drive the engine-free code with
`InMemoryLocalizationSource`; the Unity tests cover the file loader (both branches, the
`UnityWebRequest` one forced with a `file://` URL), the controller and the culture behaviour.
`SetupExampleTests` runs the setup example above and checks that `MODULE.md` and `README.md` show the
same code. Three facade tests in `DiagnosticsTests` compile only with `PFOUND_LOCALIZATION_QA`.
