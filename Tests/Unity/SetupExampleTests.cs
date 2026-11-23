using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace PFound.LocalizationService.Unity.Tests
{
    /// <summary>
    /// Runs the setup example from MODULE.md ("Setup / wiring") and README.md ("Quick reference"), and checks
    /// that both files still show that same code with the statements run here, so the docs cannot drift
    /// from what is tested.
    /// </summary>
    [TestFixture]
    public sealed class SetupExampleTests : CultureSandbox
    {
        private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(20);

        // Statements of the documented example that this test runs unchanged (or names below where not).
        private static readonly string[] DocumentedStatements =
        {
            "var (definitions, content) = await TableFileLoader.LoadFromStreamingAssetsAsync();",
            "var fallback = new LanguageKey(\"en-US\");",
            "var catalog = new LocalizationCatalog(content, fallback, definitions);",
            "var controller = new UnityLocalizationController(catalog, null, fallback);",
            "controller.ActivateStartupLanguage();",
            "string title = catalog.Get(\"menu.title\");",
            "controller.SwitchLanguage(new LanguageKey(\"tr-TR\"));",
        };

        private string _tables;

        [SetUp]
        public void WriteTables()
        {
            _tables = Path.Combine(Path.GetTempPath(), "pf_loc_setup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tables);
            File.WriteAllText(Path.Combine(_tables, "LocalizationDefinitions.txt"),
                "[ParameterDefinitions]\n[DynamicKeyDefinitions]\n[LanguageDefinitions]\nen-US=English\ntr-TR=Turkish\n[End]\n");
            File.WriteAllText(Path.Combine(_tables, "LocalizationText.txt"),
                "[en-US]\nmenu.title=Main menu\n[tr-TR]\nmenu.title=Ana menu\n");
        }

        [TearDown]
        public void DeleteTables()
        {
            try { Directory.Delete(_tables, true); } catch { }
        }

        [UnityTest]
        public IEnumerator DocumentedSetupExampleRuns()
        {
            string example = DocumentedExample();
            foreach (string statement in DocumentedStatements)
                Assert.That(example.Contains(statement), Is.True, "the documented example contains: " + statement);

            // Differences from the documented example, all forced by running it as a test:
            // 1. The tables come from a temporary folder through the internal location seam, not from
            //    StreamingAssets: ProjectFoundation has no StreamingAssets folder and a test must not add one.
            // 2. An in-memory preference store replaces null (PlayerPrefs), so the Editor's prefs stay untouched.
            // 3. The device culture is fr-FR, which the tables lack, so the startup default decides.
            // 4. The await becomes polling: an EditMode test must not block the main thread.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var load = TableFileLoader.LoadFromLocationAsync(_tables);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!load.IsCompleted)
            {
                if (watch.Elapsed > LoadTimeout) Assert.Fail("The table load did not finish within " + LoadTimeout.TotalSeconds + " s.");
                yield return null;
            }
            var (definitions, content) = load.GetAwaiter().GetResult();

            var fallback = new LanguageKey("en-US");
            var catalog = new LocalizationCatalog(content, fallback, definitions);

            var controller = new UnityLocalizationController(catalog, new MemoryPreferenceStore(), fallback);
            controller.ActivateStartupLanguage();

            string title = catalog.Get("menu.title");
            controller.SwitchLanguage(new LanguageKey("tr-TR"));

            Assert.That(title, Is.EqualTo("Main menu"), "startup falls back to the default, en-US");
            Assert.That(controller.ActiveLanguage.Code, Is.EqualTo("tr-TR"));
            Assert.That(catalog.Get("menu.title"), Is.EqualTo("Ana menu"));
        }

        // The C# block with LoadFromStreamingAssetsAsync in MODULE.md and in README.md; the two must be equal.
        private static string DocumentedExample([CallerFilePath] string thisFile = "")
        {
            string moduleRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile), "..", ".."));
            string module = ExampleBlock(Path.Combine(moduleRoot, "MODULE.md"));
            string readme = ExampleBlock(Path.Combine(moduleRoot, "README.md"));
            Assert.That(readme, Is.EqualTo(module), "README.md shows the same example as MODULE.md");
            return module;
        }

        private static string ExampleBlock(string path)
        {
            Assert.That(File.Exists(path), Is.True, "doc file exists: " + path);
            string block = Regex.Matches(File.ReadAllText(path), "```csharp\r?\n(.*?)```", RegexOptions.Singleline)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .FirstOrDefault(b => b.Contains("LoadFromStreamingAssetsAsync"));
            Assert.That(block, Is.Not.Null, "the setup example in " + path);
            return block;
        }
    }
}
