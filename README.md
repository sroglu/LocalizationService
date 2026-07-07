# PFound.LocalizationService

Key→text localization: a pluggable content source, active/fallback language resolution, parameter
formatting and `{tag}` substitution, device/preference-driven startup language, and a
CSV/Google-Sheets authoring pipeline. Engine-free synchronous core + a thin Unity glue layer. No
MonoBehaviour host — you own plain objects.

## Quick reference

```csharp
var (definitions, content) = TableFileLoader.LoadFrom(TableFileLoader.StreamingAssetsTablesDir);
var catalog     = new LocalizationCatalog(content, new LanguageKey("en"), definitions);
var controller  = new UnityLocalizationController(catalog);
controller.ActivateStartupLanguage();               // once at boot, before any UI text

string title = catalog.Get("menu.title");
controller.SwitchLanguage(new LanguageKey("tr"));   // persists + reloads + sets thread culture
```

Build order: `ILocalizationSource` → `LocalizationCatalog` → `UnityLocalizationController` →
`ActivateStartupLanguage()`.

## Dependencies

`PFound.Compression` (Unity layer, `.lzma` tables); `PFound.Utilities.EditorHelpers` (editor tools only).

## Docs

Deep reference: [MODULE.md](MODULE.md) — API, wiring, extension points, editor pipeline, limitations.
