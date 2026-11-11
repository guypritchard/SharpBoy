using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GB.Emulator.Hosting;
using GB.Emulator.Windows.Desktop;

namespace GB.Emulator.RenderProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "artifacts/render-performance/probe.json");
        int seconds = int.Parse(args.ElementAtOrDefault(1) ?? "6");
        int result = 1;
        var samples = new List<object>();
        var slowOperations = new List<object>();
        var app = new System.Windows.Application();
        var window = new DesktopWindow([]); window.Host.Muted = !args.Contains("--audio");
        window.Loaded += async (_, _) =>
        {
            try
            {
                await window.LoadRomAsync(Path.Combine(AppContext.BaseDirectory, "SPRITE.GB"));
                var warmup = Stopwatch.StartNew();
                while (window.Host.Inspection.CaptureHardware().StartingUp && warmup.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(100);
                if (window.Host.Inspection.CaptureHardware().StartingUp) throw new TimeoutException("Native startup did not finish.");
                await Task.Delay(2000);
                window.DismissHint();
                await Measure("console", moving: false);
                if (args.Contains("--transitions")) await Measure("console-transitions", moving: false, switching: true);
                window.OpenLinkLobby(); window.LinkLobby!.Hide();
                await Task.Delay(2000);
                window.DismissHint();
                await Measure("linked", moving: false);
                await Measure("linked-moving", moving: true);
                if (args.Contains("--transitions")) await Measure("linked-transitions", moving: false, switching: true);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, JsonSerializer.Serialize(new { RenderingTier = RenderCapability.Tier >> 16, Samples = samples, SlowOperations = slowOperations }, new JsonSerializerOptions { WriteIndented = true }));
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { window.Close(); }
        };
        app.Run(window); return result;

        async Task Measure(string name, bool moving, bool switching = false)
        {
            using var process = Process.GetCurrentProcess();
            var clock = Stopwatch.StartNew(); var cpu = process.TotalProcessorTime;
            long cycles = window.Host.Statistics.Cycles, frames = window.Host.Statistics.Frames;
            long presented = window.Scene.PresentedFrames, deferred = window.Scene.DeferredPresentations;
            var audioBefore = window.AudioStatistics;
            long allocated = GC.GetTotalAllocatedBytes(); int renders = 0;
            double lastRender = 0, maxRenderGap = 0, dragMilliseconds = 0;
            void OnRender(object? sender, EventArgs e)
            {
                double now = clock.Elapsed.TotalMilliseconds;
                if (renders != 0) maxRenderGap = Math.Max(maxRenderGap, now - lastRender);
                lastRender = now; renders++;
            }
            var motion = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(switching ? 900 : 33) };
            motion.Tick += (_, _) =>
            {
                long start = Stopwatch.GetTimestamp();
                if (switching) window.ToggleScreenCloseUp();
                else window.Scene.SetView(-14 + Math.Sin(clock.Elapsed.TotalSeconds * 2) * 20, -5);
                dragMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            };
            CompositionTarget.Rendering += OnRender;
            var operations = new Dictionary<DispatcherOperation, long>();
            void Started(object? sender, DispatcherHookEventArgs e) => operations[e.Operation] = Stopwatch.GetTimestamp();
            void Completed(object? sender, DispatcherHookEventArgs e)
            {
                if (!operations.Remove(e.Operation, out long started)) return;
                double duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (duration > 25 && slowOperations.Count < 80)
                {
                    var method = typeof(DispatcherOperation).GetField("_method", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(e.Operation) as Delegate;
                    slowOperations.Add(new { Phase = name, Priority = e.Operation.Priority.ToString(), Milliseconds = duration, Method = method?.Method.DeclaringType?.FullName + "." + method?.Method.Name });
                }
            }
            if (args.Contains("--trace")) { window.Dispatcher.Hooks.OperationStarted += Started; window.Dispatcher.Hooks.OperationCompleted += Completed; }
            try { if (moving || switching) motion.Start(); await Task.Delay(TimeSpan.FromSeconds(seconds)); }
            finally
            {
                motion.Stop(); CompositionTarget.Rendering -= OnRender;
                if (args.Contains("--trace")) { window.Dispatcher.Hooks.OperationStarted -= Started; window.Dispatcher.Hooks.OperationCompleted -= Completed; }
            }
            if (window.Host.Fault != null) throw window.Host.Fault;
            double elapsed = clock.Elapsed.TotalSeconds;
            var sample = new
            {
                Phase = name, Seconds = elapsed,
                Speed = (window.Host.Statistics.Cycles - cycles) / (elapsed * EmulationPacer.CyclesPerSecond),
                GuestFps = (window.Host.Statistics.Frames - frames) / elapsed,
                LcdPresentationFps = (window.Scene.PresentedFrames - presented) / elapsed,
                DeferredPresentations = window.Scene.DeferredPresentations - deferred,
                CompositionFps = renders / elapsed, MaxRenderGapMilliseconds = maxRenderGap,
                CpuMillisecondsPerSecond = (process.TotalProcessorTime - cpu).TotalMilliseconds / elapsed,
                AllocatedBytesPerSecond = (GC.GetTotalAllocatedBytes() - allocated) / elapsed,
                DragMilliseconds = dragMilliseconds, CableSleeping = window.Scene.Cable?.Simulation.IsSleeping,
                Width = window.Width, Height = window.Height,
                ViewportWidth = window.Scene.View.ActualWidth, ViewportHeight = window.Scene.View.ActualHeight,
                AudioEnabled = !window.Host.Muted, AudioAvailable = audioBefore != null,
                AudioUnderruns = window.AudioStatistics?.Underruns - audioBefore?.Underruns,
                AudioDiscardedBlocks = window.AudioStatistics?.DiscardedBlocks - audioBefore?.DiscardedBlocks
            };
            samples.Add(sample); Console.WriteLine(JsonSerializer.Serialize(sample));
            if (switching)
            {
                while (window.IsViewTransitioning) await Task.Delay(20);
                if (window.IsScreenCloseUp) { window.ToggleScreenCloseUp(); while (window.IsViewTransitioning) await Task.Delay(20); }
            }
        }
    }
}
