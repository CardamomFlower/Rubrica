using System.Windows.Media;
using System.Windows.Shapes;

namespace Rubrica.Ui
{
    /// The design's inline SVG icons. WPF reads the same path syntax as SVG, so the data
    /// strings are copied from the canvas as they are. All are drawn on a 24 x 24 grid.
    static class Icons
    {
        public const string Phone = "M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7A2 2 0 0 1 22 16.92z";
        public const string Mail = "M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M22 6l-10 7L2 6";
        public const string Chat = "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z";
        public const string Cross = "M18 6L6 18M6 6l12 12";
        public const string Star = "M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z";

        /// An icon scaled to size x size pixels. strokeWidth is in grid units, as in the SVG.
        public static Path Make(string data, double size, Brush stroke, double strokeWidth, Brush fill = null)
        {
            double scale = size / 24;
            var geometry = Geometry.Parse(data).Clone();
            geometry.Transform = new ScaleTransform(scale, scale);
            geometry.Freeze();

            return new Path
            {
                Data = geometry,
                Width = size,
                Height = size,
                Stroke = stroke,
                StrokeThickness = strokeWidth * scale,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Fill = fill,
                IsHitTestVisible = false,
            };
        }
    }
}
