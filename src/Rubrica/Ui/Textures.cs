using System;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rubrica.Ui
{
    /// The design's two SVG filter textures (leather grain, paper grain), generated when a
    /// view that wears them is built (the binder and the cover once each, Setup's inside
    /// cover once per visit, let go of when Setup is left) instead of shipping image files.
    ///
    /// In the design both are "mix-blend-mode: multiply" layers. WPF has no blend modes,
    /// but multiplying by a grey value g is the same as covering with black at alpha 1 - g,
    /// so each texture is produced directly as a see-through overlay bitmap.
    static class Textures
    {
        /// Filter "leather": fractal noise (0.055, 4 octaves, seed 7) used as a height map and
        /// lit by a distant white light (azimuth 130, elevation 58, surfaceScale 1.8);
        /// multiplied over the cover at 70% opacity.
        public static BitmapSource Leather(int width, int height)
        {
            byte[] heightMap = new PerlinNoise(7).FractalAlpha(width, height, 0.055, 4);

            const double surfaceScale = 1.8 / 255;   // the height map holds 0-255 values
            const double azimuth = 130 * Math.PI / 180, elevation = 58 * Math.PI / 180;
            double lx = Math.Cos(azimuth) * Math.Cos(elevation);
            double ly = Math.Sin(azimuth) * Math.Cos(elevation);
            double lz = Math.Sin(elevation);

            byte[] pixels = new byte[width * height * 4];
            Parallel.For(0, height, y =>
            {
                int up = Math.Max(y - 1, 0) * width, mid = y * width, down = Math.Min(y + 1, height - 1) * width;
                for (int x = 0; x < width; x++)
                {
                    int l = Math.Max(x - 1, 0), r = Math.Min(x + 1, width - 1);

                    // Surface normal from the Sobel gradient of the height map.
                    double sx = (heightMap[up + r] + 2 * heightMap[mid + r] + heightMap[down + r]
                               - heightMap[up + l] - 2 * heightMap[mid + l] - heightMap[down + l]) / 4.0;
                    double sy = (heightMap[down + l] + 2 * heightMap[down + x] + heightMap[down + r]
                               - heightMap[up + l] - 2 * heightMap[up + x] - heightMap[up + r]) / 4.0;
                    double nx = -surfaceScale * sx, ny = -surfaceScale * sy;
                    double light = (nx * lx + ny * ly + lz) / Math.Sqrt(nx * nx + ny * ny + 1);
                    light = Math.Min(Math.Max(light, 0), 1);

                    // SVG filters work in linear light; the result is shown in sRGB.
                    double grey = LinearToSrgb(light);
                    pixels[(mid + x) * 4 + 3] = (byte)Math.Round(0.7 * (1 - grey) * 255);   // black, alpha only
                }
            });
            return ToBitmap(pixels, width, height);
        }

        /// Filter "paper": fine fractal noise (0.85, 2 octaves, seed 3) recoloured to a brown
        /// whose alpha follows the noise (feColorMatrix), multiplied over the page at 40% opacity.
        public static BitmapSource Paper(int width, int height)
        {
            byte[] noise = new PerlinNoise(3).FractalAlpha(width, height, 0.85, 2);

            // The brown (0.36, 0.29, 0.18 in linear light = 0.634, 0.576, 0.462 in sRGB) multiplied
            // over cream paper at alpha a equals this premultiplied overlay, per unit of a.
            const double red = 0.162, green = 0.104, blue = 0, alpha = 0.538;

            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < noise.Length; i++)
            {
                double a = 0.4 * 0.45 * noise[i];   // layer opacity x matrix alpha x noise (0-255)
                pixels[i * 4 + 0] = (byte)Math.Round(blue * a);
                pixels[i * 4 + 1] = (byte)Math.Round(green * a);
                pixels[i * 4 + 2] = (byte)Math.Round(red * a);
                pixels[i * 4 + 3] = (byte)Math.Round(alpha * a);
            }
            return ToBitmap(pixels, width, height);
        }

        static double LinearToSrgb(double v)
        {
            return v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
        }

        static BitmapSource ToBitmap(byte[] premultipliedBgra, int width, int height)
        {
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, premultipliedBgra, width * 4);
            bitmap.Freeze();
            return bitmap;
        }
    }

    /// Port of the reference implementation of feTurbulence from the SVG specification
    /// (Perlin noise with the Park-Miller random generator), which browsers follow.
    /// Only the alpha channel is evaluated: it is the only one the two filters use.
    sealed class PerlinNoise
    {
        const int BSize = 0x100, BM = 0xff, PerlinN = 0x1000;
        const int AlphaChannel = 3;

        readonly int[] lattice = new int[BSize + BSize + 2];
        readonly double[] gradientX = new double[BSize + BSize + 2];
        readonly double[] gradientY = new double[BSize + BSize + 2];

        public PerlinNoise(long seed)
        {
            if (seed <= 0) seed = -(seed % (RandM - 1)) + 1;
            if (seed > RandM - 1) seed = RandM - 1;

            // The generator is shared by the four colour channels, so all of them are drawn
            // to keep the sequence identical to the reference; only alpha is stored.
            for (int channel = 0; channel < 4; channel++)
            {
                for (int i = 0; i < BSize; i++)
                {
                    lattice[i] = i;
                    double gx = (double)(((seed = Random(seed)) % (BSize + BSize)) - BSize) / BSize;
                    double gy = (double)(((seed = Random(seed)) % (BSize + BSize)) - BSize) / BSize;
                    if (channel != AlphaChannel) continue;
                    double length = Math.Sqrt(gx * gx + gy * gy);
                    gradientX[i] = gx / length;
                    gradientY[i] = gy / length;
                }
            }
            for (int i = BSize - 1; i > 0; i--)
            {
                int j = (int)((seed = Random(seed)) % BSize);
                int swap = lattice[i];
                lattice[i] = lattice[j];
                lattice[j] = swap;
            }
            for (int i = 0; i < BSize + 2; i++)
            {
                lattice[BSize + i] = lattice[i];
                gradientX[BSize + i] = gradientX[i];
                gradientY[BSize + i] = gradientY[i];
            }
        }

        /// type="fractalNoise": the alpha channel of every pixel, as 0-255.
        public byte[] FractalAlpha(int width, int height, double baseFrequency, int octaves)
        {
            byte[] result = new byte[width * height];
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    // Browsers count noise coordinates from 1: with the +1 the grain is theirs, dot for dot.
                    double vx = (x + 1) * baseFrequency, vy = (y + 1) * baseFrequency, sum = 0, ratio = 1;
                    for (int octave = 0; octave < octaves; octave++)
                    {
                        sum += Noise(vx, vy) / ratio;
                        vx *= 2;
                        vy *= 2;
                        ratio *= 2;
                    }
                    double value = (sum + 1) / 2;
                    result[y * width + x] = (byte)Math.Round(Math.Min(Math.Max(value, 0), 1) * 255);
                }
            });
            return result;
        }

        double Noise(double vx, double vy)
        {
            double t = vx + PerlinN;
            int bx0 = (int)t & BM, bx1 = (bx0 + 1) & BM;
            double rx0 = t - (int)t, rx1 = rx0 - 1;
            t = vy + PerlinN;
            int by0 = (int)t & BM, by1 = (by0 + 1) & BM;
            double ry0 = t - (int)t, ry1 = ry0 - 1;

            int i = lattice[bx0], j = lattice[bx1];
            int b00 = lattice[i + by0], b10 = lattice[j + by0], b01 = lattice[i + by1], b11 = lattice[j + by1];

            double sx = rx0 * rx0 * (3 - 2 * rx0), sy = ry0 * ry0 * (3 - 2 * ry0);

            double u = rx0 * gradientX[b00] + ry0 * gradientY[b00];
            double v = rx1 * gradientX[b10] + ry0 * gradientY[b10];
            double a = u + sx * (v - u);
            u = rx0 * gradientX[b01] + ry1 * gradientY[b01];
            v = rx1 * gradientX[b11] + ry1 * gradientY[b11];
            double b = u + sx * (v - u);
            return a + sy * (b - a);
        }

        // Park-Miller "minimal standard" generator: r = (16807 * r) mod (2^31 - 1).
        const long RandM = 2147483647, RandA = 16807, RandQ = 127773, RandR = 2836;

        static long Random(long seed)
        {
            long result = RandA * (seed % RandQ) - RandR * (seed / RandQ);
            if (result <= 0) result += RandM;
            return result;
        }
    }
}
