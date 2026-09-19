using System;
using System.Windows;
using System.Windows.Media;

namespace Rubrica.Ui
{
    /// One embedded font: the WPF family (for standard controls such as text boxes) and
    /// the raw glyph face (for GlyphText).
    sealed class FontSpec
    {
        public readonly FontFamily Family;
        public readonly GlyphTypeface Face;

        public FontSpec(FontFamily family, GlyphTypeface face)
        {
            Family = family;
            Face = face;
        }

        // A browser rounds ascent and descent to whole pixels at every font size.
        public double Ascent(double size) { return Math.Floor(Face.Baseline * size + 0.5); }
        public double Descent(double size) { return Math.Floor((Face.Height - Face.Baseline) * size + 0.5); }
    }

    static class AppFonts
    {
        /// Handwriting: names, numbers, page titles.
        public static readonly FontSpec Hand;

        /// Typewriter: labels, tabs, page numbers.
        public static readonly FontSpec Type;

        /// A system font for the rare character the two above do not have (they cover
        /// Western European text; a Cyrillic or Greek name would need this).
        public static readonly GlyphTypeface Fallback;

        static AppFonts()
        {
            // "pack://" addresses only parse once WPF has registered the scheme, and nothing
            // guarantees that already happened: reading this property registers it.
            GC.KeepAlive(System.IO.Packaging.PackUriHelper.UriSchemePack);

            Hand = Load("Patrick Hand SC");
            Type = Load("Special Elite");

            if (!new Typeface("Segoe UI").TryGetGlyphTypeface(out Fallback))
                Fallback = Hand.Face;
        }

        static FontSpec Load(string familyName)
        {
            // The .ttf files are compiled into the exe (see Rubrica.csproj).
            var family = new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#" + familyName);
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            GlyphTypeface face;
            bool found = typeface.TryGetGlyphTypeface(out face);

            // WPF silently falls back to a system font when a family cannot be resolved.
            if (!found || !face.FamilyNames.Values.Contains(familyName))
                throw new InvalidOperationException("Embedded font not found: " + familyName);

            return new FontSpec(family, face);
        }
    }
}
