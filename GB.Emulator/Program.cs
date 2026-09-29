using GB.Emulator.Core;
using GB.Emulator.Display;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GB.Emulator
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            if (Console.IsOutputRedirected)
            {
                Console.Error.WriteLine("Console video requires an interactive terminal.");
                return;
            }

            if (Console.WindowWidth < ConsoleVideoRenderer.Columns ||
                Console.WindowHeight < ConsoleVideoRenderer.Rows + 1)
            {
                Console.Error.WriteLine(
                    $"Console video requires at least {ConsoleVideoRenderer.Columns} columns and " +
                    $"{ConsoleVideoRenderer.Rows + 1} rows; resize the terminal and try again.");
                return;
            }

            var gameboy = new Gameboy();
            string romPath = args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.Combine(AppContext.BaseDirectory, "SPRITE.GB");
            var cartridge = await CartridgeLoader.Load(romPath);

            gameboy.Load(cartridge);
            Console.OutputEncoding = Encoding.UTF8;
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                e.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += onCancel;
            bool previousCursorVisible = !OperatingSystem.IsWindows() || Console.CursorVisible;
            try
            {
                Console.CursorVisible = false;
                using var screen = new ConsoleScreen(gameboy);
                using var keyboard = new ConsoleKeyboardInput(gameboy.Input);
                screen.Redraw(clear: true);
                int inputPoll = 0;
                while (!cancellation.IsCancellationRequested)
                {
                    CpuStepResult step = gameboy.Step();
                    if (step.Instruction.Name == "STOP idle")
                    {
                        if (keyboard.Poll()) screen.Redraw(clear: true);
                        Thread.Sleep(10);
                        continue;
                    }

                    if ((++inputPoll & 0xFF) == 0 && keyboard.Poll())
                    {
                        screen.Redraw(clear: true);
                    }
                }
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
                Console.ResetColor();
                Console.CursorVisible = previousCursorVisible;
                Console.SetCursorPosition(0, ConsoleVideoRenderer.Rows);
                Console.WriteLine();
            }
        }

    }
}
