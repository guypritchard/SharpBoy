using GB.Emulator.Core;
using GB.Emulator.Display;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Text.RegularExpressions;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class ConsoleVideoRendererTests
    {
        [TestMethod]
        public void Render_PacksTwoScanlinesIntoEachTerminalRow()
        {
            var frame = new byte[Video.Width * Video.Height];
            frame[Video.Width] = 3; // Lower pixel of the first cell.
            frame[1] = 2;
            frame[Video.Width + 1] = 2; // Equal shades use a full block.
            frame[Video.Width * 2] = 1; // Upper pixel of the next terminal row.

            string output = ConsoleVideoRenderer.Render(frame);
            string cells = Regex.Replace(output, "\x1b\\[[0-9;]*[mH]", "");

            Assert.AreEqual(ConsoleVideoRenderer.Columns * ConsoleVideoRenderer.Rows, cells.Length);
            Assert.HasCount(72, Regex.Matches(output, "\x1b\\[[0-9]+;1H"));

            Assert.AreEqual('▀', cells[0]);
            Assert.AreEqual('█', cells[1]);
            Assert.AreEqual('▀', cells[ConsoleVideoRenderer.Columns]);
            StringAssert.Contains(output, "\x1b[38;2;224;248;208m\x1b[48;2;8;24;32m▀");
            StringAssert.EndsWith(output, "\x1b[0m");
        }

        [TestMethod]
        public void Render_RejectsInvalidFrames()
        {
            Assert.ThrowsExactly<ArgumentException>(() => ConsoleVideoRenderer.Render(new byte[1]));

            var frame = new byte[Video.Width * Video.Height];
            frame[0] = 4;
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ConsoleVideoRenderer.Render(frame));
        }

        [TestMethod]
        public void RenderChanges_WritesOnlyChangedTerminalRows()
        {
            var previous = new byte[Video.Width * Video.Height];
            var current = (byte[])previous.Clone();
            current[Video.Width * 5 + 3] = 2;

            string output = ConsoleVideoRenderer.RenderChanges(current, previous);

            Assert.HasCount(1, Regex.Matches(output, "\x1b\\[[0-9]+;[0-9]+H"));
            StringAssert.StartsWith(output, "\x1b[3;4H");
            Assert.HasCount(1, Regex.Matches(output, "[█▀]"));
            Assert.AreEqual(string.Empty, ConsoleVideoRenderer.RenderChanges(current, current));
        }
    }
}
