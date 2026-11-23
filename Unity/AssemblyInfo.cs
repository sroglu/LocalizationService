using System.Runtime.CompilerServices;

// The Unity test assembly drives the internal table-location seam (TableFileLoader.LoadFromLocationAsync),
// which forces the UnityWebRequest branch in the Editor with a file:// URL.
[assembly: InternalsVisibleTo("PFound.LocalizationService.Unity.Tests")]
