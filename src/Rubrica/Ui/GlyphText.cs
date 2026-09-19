using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Rubrica.Ui
{
    /// One line of text drawn glyph by glyph. WPF 4.6 has no letter-spacing, and the
    /// design uses it on almost every label, so the glyph advances are set by hand here.
    /// The element is sized like a CSS line box: text width (letter-spacing included)
    /// by line-height, with the baseline where a browser would put it.
    sealed class GlyphText : FrameworkElement
    {
        /// Consecutive glyphs that come from the same font file.
        sealed class Run
        {
            public GlyphTypeface Face;
            public readonly List<ushort> Glyphs = new List<ushort>();
            public readonly List<double> Advances = new List<double>();
            public double Width;
        }

        readonly FontSpec font;
        readonly string text;
        readonly double size, letterSpacing, baseline, lineHeight;
        readonly List<Run> runs = new List<Run>();
        double width;
        Brush brush;

        /// Optional CSS text-shadow: the same glyphs painted first, moved by ShadowOffset.
        public Brush ShadowBrush;
        public Vector ShadowOffset;

        int markStart, markLength;   // the part drawn on a yellow marker (<mark>): none by default

        /// lineHeight: CSS line-height in pixels; NaN means "normal".
        /// maxWidth: text that does not fit is cut and ends in "...".
        public GlyphText(string text, FontSpec font, double size, Color color, double letterSpacing = 0,
                         double lineHeight = double.NaN, double maxWidth = double.PositiveInfinity)
        {
            this.font = font;
            this.text = text;
            this.size = size;
            this.letterSpacing = letterSpacing;
            brush = Css.Fill(color);
            IsHitTestVisible = false;   // clicks go to whatever the text is written on

            double ascent = font.Ascent(size), descent = font.Descent(size);
            this.lineHeight = double.IsNaN(lineHeight) ? ascent + descent : lineHeight;
            // Half of the extra (or missing) line height goes above the text, rounded down.
            baseline = ascent + Math.Floor((this.lineHeight - (ascent + descent)) / 2);

            Layout(text);
            if (width > maxWidth) Cut(maxWidth);
        }

        /// Keeps as many characters as fit with "..." after them. The search starts from a guess
        /// (widths are roughly proportional) and walks to the exact edge from there, instead of
        /// laying the whole text out again for every character taken off its end.
        void Cut(double maxWidth)
        {
            int keep = Math.Max(1, Math.Min(text.Length - 1, (int)(text.Length * maxWidth / width)));
            Layout(Shortened(keep));
            while (width <= maxWidth && keep < text.Length - 1)      // too cautious: take characters back
            {
                Layout(Shortened(keep + 1));
                if (width > maxWidth) break;
                keep++;
            }
            while (width > maxWidth && keep > 1) Layout(Shortened(--keep));   // too long: take more off
            Layout(Shortened(keep));
        }

        string Shortened(int keep)
        {
            return text.Substring(0, keep).TrimEnd() + "...";
        }

        /// What it was asked to write - in full, even when it is drawn cut short.
        public string Text { get { return text; } }

        /// Draws the characters from start, length of them, over a yellow marker, as the
        /// search results mark what was found.
        public void Mark(int start, int length)
        {
            markStart = start;
            markLength = length;
            InvalidateVisual();
        }

        public double TextWidth { get { return width; } }
        public double LineHeight { get { return lineHeight; } }

        public Color Foreground
        {
            set
            {
                brush = Css.Fill(value);
                InvalidateVisual();   // WPF: ask for OnRender to be called again
            }
        }

        /// Several lines: a word that would cross maxWidth moves to the next line, as in a browser.
        public static StackPanel Wrap(string text, FontSpec font, double size, Color color,
                                      double letterSpacing, double lineHeight, double maxWidth)
        {
            var lines = new StackPanel();
            string line = "";
            foreach (string whole in text.Split(' '))
            foreach (string word in Pieces(whole, font, size, color, letterSpacing, maxWidth))
            {
                string longer = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && new GlyphText(longer, font, size, color, letterSpacing).TextWidth > maxWidth)
                {
                    lines.Children.Add(new GlyphText(line, font, size, color, letterSpacing, lineHeight) { HorizontalAlignment = HorizontalAlignment.Left });
                    line = word;
                }
                else
                {
                    line = longer;
                }
            }
            lines.Children.Add(new GlyphText(line, font, size, color, letterSpacing, lineHeight) { HorizontalAlignment = HorizontalAlignment.Left });
            return lines;
        }

        /// A word wider than a whole line - a long file name - is cut where the line ends, as
        /// overflow-wrap: anywhere does. Any other word comes back as it is.
        static IEnumerable<string> Pieces(string word, FontSpec font, double size, Color color, double letterSpacing, double maxWidth)
        {
            while (word.Length > 1 && new GlyphText(word, font, size, color, letterSpacing).TextWidth > maxWidth)
            {
                int fits = 1;
                while (fits + 1 < word.Length && new GlyphText(word.Substring(0, fits + 1), font, size, color, letterSpacing).TextWidth <= maxWidth) fits++;
                yield return word.Substring(0, fits);
                word = word.Substring(fits);
            }
            yield return word;
        }

        /// Turns the characters into glyphs. A character the design font lacks is taken
        /// from the system font instead of being drawn as an empty box.
        void Layout(string text)
        {
            runs.Clear();
            width = 0;
            foreach (char c in text)
            {
                GlyphTypeface face = font.Face;
                ushort glyph;
                if (!face.CharacterToGlyphMap.TryGetValue(c, out glyph))
                {
                    face = AppFonts.Fallback;
                    if (!face.CharacterToGlyphMap.TryGetValue(c, out glyph))
                    {
                        face = font.Face;
                        glyph = 0;   // the font's own "missing" box
                    }
                }

                if (runs.Count == 0 || runs[runs.Count - 1].Face != face)
                    runs.Add(new Run { Face = face });
                Run run = runs[runs.Count - 1];

                double advance = face.AdvanceWidths[glyph] * size + letterSpacing;
                run.Glyphs.Add(glyph);
                run.Advances.Add(advance);
                run.Width += advance;
                width += advance;
            }
        }

        // WPF layout: how much room the element asks for.
        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(width, lineHeight);
        }

        // WPF drawing: the equivalent of JUCE's paint().
        protected override void OnRender(DrawingContext dc)
        {
            if (markLength > 0) dc.DrawRectangle(Marker, null, MarkedArea());

            double x = 0;
            foreach (Run run in runs)
            {
                if (ShadowBrush != null)
                    dc.DrawGlyphRun(ShadowBrush, Glyphs(run, new Point(x + ShadowOffset.X, baseline + ShadowOffset.Y)));
                dc.DrawGlyphRun(brush, Glyphs(run, new Point(x, baseline)));
                x += run.Width;
            }
        }

        static readonly Brush Marker = Css.Fill(Css.Rgba(250, 220, 80, 0.75));

        /// The marker covers the marked glyphs, as tall as the font's own line (ascent plus
        /// descent), whatever the line height around it - which is how a browser paints <mark>.
        Rect MarkedArea()
        {
            double left = 0, right = 0;
            int i = 0;
            foreach (Run run in runs)
                foreach (double advance in run.Advances)
                {
                    if (i < markStart) left += advance;
                    if (i < markStart + markLength) right += advance;
                    i++;
                }
            double ascent = font.Ascent(size), descent = font.Descent(size);
            return new Rect(left, baseline - ascent, Math.Max(0, right - left), ascent + descent);
        }

        GlyphRun Glyphs(Run run, Point origin)
        {
            return new GlyphRun(run.Face, 0, false, size, run.Glyphs, origin, run.Advances,
                                null, null, null, null, null, null);
        }
    }
}
