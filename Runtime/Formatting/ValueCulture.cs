using System;
using System.Globalization;

namespace PFound.LocalizationService
{
    /// <summary>
    /// The culture the built-in parameter values format and parse under. A catalog lookup sets it to the
    /// catalog's culture for its duration, so values render in the language's culture while the thread and
    /// app culture stay as they are. Outside a lookup it is the thread's current culture.
    /// </summary>
    internal static class ValueCulture
    {
        [ThreadStatic] private static CultureInfo t_scoped;

        public static CultureInfo Current => t_scoped ?? CultureInfo.CurrentCulture;

        /// <summary>Makes <paramref name="culture"/> current until the returned scope is disposed.</summary>
        public static Scope Use(CultureInfo culture) => new Scope(culture);

        public readonly struct Scope : IDisposable
        {
            private readonly CultureInfo _previous;

            internal Scope(CultureInfo culture)
            {
                _previous = t_scoped;
                t_scoped = culture;
            }

            public void Dispose() => t_scoped = _previous;
        }
    }
}
