using System;
using System.Text;
using GB.Emulator.Core;

namespace GB.Emulator.Display
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
            ValidateFrame(frame, nameof(frame));
            return RenderRows(frame, default, false);
        }

        /// <summary>Draws only terminal rows whose pixels differ from the previous frame.</summary>
        public static string RenderChanges(ReadOnlySpan<byte> frame, ReadOnlySpan<byte> previousFrame)
        {
            ValidateFrame(frame, nameof(frame));
            ValidateFrame(previousFrame, nameof(previousFrame));
            return RenderRows(frame, previousFrame, true);
        }

        private static string RenderRows(ReadOnlySpan<byte> frame, ReadOnlySpan<byte> previousFrame, bool changedOnly)
        {
            var output = new StringBuilder();
            int lastForeground = -1;
            int lastBackground = -1;

            for (int row = 0; row < Rows; row++)
            {
                int upperStart = row * 2 * Columns;
                int lowerStart = upperStart + Columns;
                int changedCells = 0;
                if (changedOnly)
                {
                    for (int column = 0; column < Columns; column++)
                        if (frame[upperStart + column] != previousFrame[upperStart + column] ||
                            frame[lowerStart + column] != previousFrame[lowerStart + column])
                            changedCells++;
                    if (changedCells == 0) continue;
                }

                // Address each row directly: a newline after column 160 could
                // advance twice in terminals that wrap at the right edge.
                bool sparse = changedOnly && changedCells < 32;
                if (!sparse) output.Append("\x1b[").Append(row + 1).Append(";1H");
                bool inRun = false;
                for (int column = 0; column < Columns; column++)
                {
                    int upper = frame[upperStart + column];
                    int lower = frame[lowerStart + column];
                    if (sparse)
                    {
                        bool changed = upper != previousFrame[upperStart + column] ||
                            lower != previousFrame[lowerStart + column];
                        if (!changed)
                        {
                            inRun = false;
                            continue;
                        }
                        if (!inRun)
                        {
                            output.Append("\x1b[").Append(row + 1).Append(';').Append(column + 1).Append('H');
                            inRun = true;
                        }
                    }
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

            if (output.Length > 0) output.Append("\x1b[0m");
            return output.ToString();
        }

        public static void Draw(ReadOnlySpan<byte> frame)
        {
            DrawText(Render(frame));
        }

        public static void DrawChanges(ReadOnlySpan<byte> frame, ReadOnlySpan<byte> previousFrame)
        {
            DrawText(RenderChanges(frame, previousFrame));
        }

        private static void DrawText(string output)
        {
            if (Console.IsOutputRedirected)
                throw new InvalidOperationException("Console video requires an interactive terminal.");
            if (Console.WindowWidth < Columns || Console.WindowHeight < Rows + 1)
                throw new InvalidOperationException($"Console video requires at least {Columns} columns and {Rows + 1} rows.");
            if (output.Length == 0) return;
            if (Console.OutputEncoding.CodePage != Encoding.UTF8.CodePage)
                Console.OutputEncoding = Encoding.UTF8;
            Console.Write(output);
        }

        private static void ValidateFrame(ReadOnlySpan<byte> frame, string parameterName)
        {
            if (frame.Length != Video.Width * Video.Height)
                throw new ArgumentException($"A frame must contain exactly {Video.Width * Video.Height} pixels.", parameterName);
        }
    }
}
