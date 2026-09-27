using System;
using System.Text;

namespace GB.Emulator.Core
{
    /// <summary>Renders a 160 by 144 frame of Game Boy shade indices (0 to 3) in a true-color terminal.</summary>
    public static class ConsoleVideoRenderer
    {
        public const int Columns = Video.Width;
        public const int Rows = Video.Height / 2;

        // The four original Game Boy shades, from lightest to darkest.
        private static readonly string[] Foreground =
        {
            "\x1b[38;2;224;248;208m",
            "\x1b[38;2;136;192;112m",
            "\x1b[38;2;52;104;86m",
            "\x1b[38;2;8;24;32m"
        };

        private static readonly string[] Background =
        {
            "\x1b[48;2;224;248;208m",
            "\x1b[48;2;136;192;112m",
            "\x1b[48;2;52;104;86m",
            "\x1b[48;2;8;24;32m"
        };

        public static string Render(ReadOnlySpan<byte> frame)
        {
            if (frame.Length != Video.Width * Video.Height)
            {
                throw new ArgumentException($"A frame must contain exactly {Video.Width * Video.Height} pixels.", nameof(frame));
            }

            var output = new StringBuilder();
            int lastForeground = -1;
            int lastBackground = -1;

            for (int row = 0; row < Rows; row++)
            {
                // Address each row directly: a newline after column 160 could
                // advance twice in terminals that wrap at the right edge.
                output.Append("\x1b[").Append(row + 1).Append(";1H");
                for (int column = 0; column < Columns; column++)
                {
                    int upper = frame[(row * 2 * Columns) + column];
                    int lower = frame[((row * 2 + 1) * Columns) + column];
                    if (upper > 3 || lower > 3)
                    {
                        throw new ArgumentOutOfRangeException(nameof(frame), "Pixel shades must be between 0 and 3.");
                    }

                    if (upper != lastForeground)
                    {
                        output.Append(Foreground[upper]);
                        lastForeground = upper;
                    }

                    if (lower != lastBackground)
                    {
                        output.Append(Background[lower]);
                        lastBackground = lower;
                    }

                    // Each cell holds two vertically adjacent pixels.
                    output.Append(upper == lower ? '█' : '▀');
                }

            }

            output.Append("\x1b[0m");
            return output.ToString();
        }

        public static void Draw(ReadOnlySpan<byte> frame)
        {
            if (Console.IsOutputRedirected)
            {
                throw new InvalidOperationException("Console video requires an interactive terminal.");
            }

            if (Console.WindowWidth < Columns || Console.WindowHeight < Rows + 1)
            {
                throw new InvalidOperationException($"Console video requires at least {Columns} columns and {Rows + 1} rows.");
            }

            string output = Render(frame);
            Console.OutputEncoding = Encoding.UTF8;
            Console.Write(output);
        }
    }
}
