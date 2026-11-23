# PFound.LocalizationService

Key→text localization: a pluggable content source, active/fallback language resolution, parameter
formatting and `{tag}` substitution, device/preference-driven startup language, and a
CSV/Google-Sheets authoring pipeline. Engine-free synchronous core + a thin Unity glue layer. No
MonoBehaviour host — you own plain objects.

## Quick reference

In an async method on the main thread, with `[en-US]` and `[tr-TR]` tables in
`StreamingAssets/Localizables/` (this loads on every platform, Android included). Await the load and
never block on it with `.Result`, `.Wait()` or `GetAwaiter().GetResult()`: on Android and WebGL it
completes on the main thread, so blocking that thread hangs.

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

A language switch changes only the catalog's culture; the app's culture stays as it was.

Build order: `ILocalizationSource` → `LocalizationCatalog` → `UnityLocalizationController` →
`ActivateStartupLanguage()`.

Optional build flags: `PFOUND_BESTHTTP` compiles `RemoteTableAcquisition` (CDN table download); define
`PFOUND_LOCALIZATION_QA` to expose `catalog.Diagnostics` (show-keys / pseudo-localization QA modes).

## Dependencies

`PFound.Compression` (Unity layer, `.lzma` tables); `PFound.Utilities.EditorHelpers` (editor tools only);
`BestHTTP` (`PFOUND_BESTHTTP`-gated remote-acquisition assembly only).

## Docs

Deep reference: [MODULE.md](MODULE.md) — API, wiring, extension points, editor pipeline, limitations.
