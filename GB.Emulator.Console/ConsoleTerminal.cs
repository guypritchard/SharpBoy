#nullable enable
using System;
using System.Text;
using System.Threading;

namespace GB.Emulator;

/// <summary>Owns the interactive terminal state for one emulator session.</summary>
internal sealed class ConsoleTerminal : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly ConsoleCancelEventHandler cancelHandler;
    private readonly bool previousCursorVisible;

    private ConsoleTerminal()
    {
        this.previousCursorVisible = !OperatingSystem.IsWindows() || Console.CursorVisible;
        this.cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            this.cancellation.Cancel();
        };
        Console.OutputEncoding = Encoding.UTF8;
        Console.CursorVisible = false;
        Console.CancelKeyPress += this.cancelHandler;
    }

    public CancellationToken Stopping => this.cancellation.Token;

    public static ConsoleTerminal? Open()
    {
        if (Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("Console video requires an interactive terminal.");
            return null;
        }

        if (Console.WindowWidth < ConsoleVideoRenderer.Columns ||
            Console.WindowHeight < ConsoleVideoRenderer.Rows + 1)
        {
            Console.Error.WriteLine(
                $"Console video requires at least {ConsoleVideoRenderer.Columns} columns and " +
                $"{ConsoleVideoRenderer.Rows + 1} rows; resize the terminal and try again.");
            return null;
        }

        return new ConsoleTerminal();
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= this.cancelHandler;
        Console.ResetColor();
        Console.CursorVisible = this.previousCursorVisible;
        if (Console.WindowHeight > ConsoleVideoRenderer.Rows)
            Console.SetCursorPosition(0, ConsoleVideoRenderer.Rows);
        Console.WriteLine();
        this.cancellation.Dispose();
    }
}
