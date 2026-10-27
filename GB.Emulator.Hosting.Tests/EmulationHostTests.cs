using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]

namespace GB.Emulator.Hosting.Tests;

[TestClass]
public sealed class EmulationHostTests
{
    private static readonly TimeSpan FrameTime = TimeSpan.FromSeconds(EmulationPacer.CyclesPerFrame / (double)EmulationPacer.CyclesPerSecond);
    private static EmulationHostOptions Options(int history = 200, bool captureFrames = true) => new()
    {
        MaximumAdvanceDuration = TimeSpan.FromSeconds(1), HistoryCapacity = history, CaptureFrames = captureFrames
    };
    private static Cartridge Rom(params byte[] instructions)
    {
        var bytes = new byte[0x8000];
        instructions.CopyTo(bytes, 0x100);
        return new Cartridge { Data = bytes };
    }
    private static EmulationHost Host(params byte[] instructions)
    {
        var host = new EmulationHost(new Gameboy(), options: Options());
        host.Load(Rom(instructions));
        return host;
    }

    [TestMethod]
    public void TwoMachinesOwnRegistersFlagsMemoryAndRestorationIndependently()
    {
        var first = new Gameboy(); first.Load(Rom(0x3E, 0x12, 0xE0, 0x80, 0x3C));
        first.Step(); first.Cpu.Flags.C = true;
        GameboyState saved = first.CaptureState();
        var second = new Gameboy(); second.Load(Rom(0x3E, 0x34, 0xE0, 0x80, 0x3D));
        Assert.AreEqual(0x12, first.Cpu.Registers.A, "Creating another CPU must not reset this machine.");
        second.Step(); first.Step(); second.Step(); first.Step(); second.Step();
        Assert.AreEqual(0x13, first.Cpu.Registers.A);
        Assert.AreEqual(0x33, second.Cpu.Registers.A);
        Assert.AreEqual(0x12, first.Memory.Peek(0xFF80));
        Assert.AreEqual(0x34, second.Memory.Peek(0xFF80));
        Assert.IsTrue(first.Cpu.Flags.C);
        Assert.IsFalse(second.Cpu.Flags.C);
        long otherCycles = second.EmulatedCycles;
        first.RestoreState(saved);
        Assert.AreEqual(0x12, first.Cpu.Registers.A);
        Assert.AreEqual(0x33, second.Cpu.Registers.A);
        Assert.AreEqual(otherCycles, second.EmulatedCycles);
    }

    [TestMethod]
    public void TransientSuspensionsCannotUndoManualPauseOrAnotherSuspension()
    {
        using var host = Host(0x18, 0xFE);
        Assert.AreEqual(EmulationState.Paused, host.State);
        host.Resume(); host.SetButtonState(GameBoyButton.A, true);
        host.SetSuspended(PauseReason.Dialog, true);
        Assert.IsFalse(host.IsPressed(GameBoyButton.A));
        host.SetSuspended(PauseReason.Inactive, true);
        host.SetSuspended(PauseReason.Dialog, false);
        Assert.AreEqual(EmulationState.Paused, host.State);
        host.Pause(); host.SetSuspended(PauseReason.Inactive, false);
        Assert.AreEqual(EmulationState.Paused, host.State);
        Assert.AreEqual(0L, host.Advance(TimeSpan.FromHours(1)));
        host.Resume();
        Assert.IsGreaterThan(0L, host.Advance(TimeSpan.FromMilliseconds(1)));
        Assert.IsLessThan(5000L, host.Statistics.Cycles, "Paused wall time must not become a catch-up backlog.");
    }

    [TestMethod]
    public void CatchUpIsBoundedAndResetPreservesTheUsersPauseChoice()
    {
        using var host = Host(0x18, 0xFE);
        host.Resume();
        long cycles = host.Advance(TimeSpan.FromHours(1));
        Assert.IsLessThanOrEqualTo(EmulationPacer.CyclesPerFrame * 2L + 12, cycles);
        host.Reset(); Assert.AreEqual(EmulationState.Running, host.State);
        Assert.AreEqual(0L, host.Statistics.Cycles);
        host.Pause(); host.Reset(); Assert.AreEqual(EmulationState.Paused, host.State);
    }

    [TestMethod]
    public void FrameSnapshotsOutliveResetAndHaveCompletedCycleMetadata()
    {
        using var host = new EmulationHost(options: Options());
        host.Load(Rom());
        host.Resume();
        host.Advance(FrameTime);
        Assert.AreEqual(EmulationPacer.CyclesPerFrame, host.LatestFrame!.Cycles);
        for (int i = 1; i < 200; i++) host.Advance(FrameTime);
        VideoFrame retained = host.LatestFrame!;
        byte[] original = retained.Pixels.ToArray();
        Assert.IsTrue(original.Any(x => x != 0), "The registered mark is visible even with a blank synthetic logo.");
        host.Reset();
        CollectionAssert.AreEqual(original, retained.Pixels.ToArray());
        Assert.IsTrue(host.LatestFrame!.Pixels.ToArray().All(x => x == 0));
    }

    [TestMethod]
    public void DisablingTheLcdBlanksTheFrameWithoutWaitingForAnotherVBlank()
    {
        var machine = new Gameboy();
        using var host = new EmulationHost(machine, options: Options());
        host.Load(Rom(0x18, 0xFE));
        machine.Memory.Write8(0xFF, 0x8000);
        machine.Memory.Write8(0xFC, 0xFF47);
        machine.Memory.Write8(0x91, 0xFF40);
        host.Resume(); host.Advance(FrameTime);
        Assert.IsTrue(host.LatestFrame!.Pixels.ToArray().Any(x => x != 0));
        long frames = host.Statistics.Frames;
        machine.Memory.Write8(0, 0xFF40);
        host.Advance(FrameTime);
        Assert.IsTrue(host.LatestFrame!.Pixels.ToArray().All(x => x == 0));
        Assert.AreEqual(frames, host.Statistics.Frames, "A presentation refresh must not count as an emulated LCD frame.");
    }

    [TestMethod]
    public void TwoHostsProduceIdenticalFramesForIdenticalExecution()
    {
        using var first = new EmulationHost(options: Options());
        using var second = new EmulationHost(options: Options());
        first.Load(Rom()); second.Load(Rom()); first.Resume(); second.Resume();
        for (int i = 0; i < 210; i++)
        {
            first.Advance(FrameTime); second.Advance(FrameTime);
            Assert.AreEqual(first.Statistics.Cycles, second.Statistics.Cycles);
            CollectionAssert.AreEqual(first.LatestFrame!.Pixels.ToArray(), second.LatestFrame!.Pixels.ToArray());
        }
    }

    [TestMethod]
    public void HeadlessExecutionCanDisableFrameCaptureAndStillReportThroughput()
    {
        using var host = new EmulationHost(options: Options(captureFrames: false));
        host.Load(Rom()); host.Resume();
        for (int i = 0; i < 60; i++) host.Advance(FrameTime);
        Assert.IsNull(host.LatestFrame);
        Assert.IsGreaterThan(0L, host.Statistics.Frames);
        Assert.IsTrue(host.Statistics.Speed > 0.98 && host.Statistics.Speed < 1.02);
        Assert.IsTrue(host.Statistics.FramesPerSecond > 59 && host.Statistics.FramesPerSecond < 61);
    }

    [TestMethod]
    public void AudioIsCopiedByTheAdapterMutedFlushedAndDisposedOnce()
    {
        var audio = new RecordingAudio();
        var host = new EmulationHost(audio: audio, options: Options());
        host.Load(Rom()); host.Resume();
        for (int i = 0; i < 3; i++) host.Advance(FrameTime);
        Assert.IsGreaterThan(0, audio.Blocks.Count);
        short[] retained = (short[])audio.Blocks[0].Clone();
        host.Pause(); Assert.IsGreaterThan(0, audio.Flushes);
        int count = audio.Blocks.Count;
        host.Muted = true; host.Resume();
        for (int i = 0; i < 3; i++) host.Advance(FrameTime);
        Assert.HasCount(count, audio.Blocks);
        CollectionAssert.AreEqual(retained, audio.Blocks[0]);
        host.Pause(); host.Muted = false;
        host.Debugging.Step();
        Assert.IsGreaterThan(count, audio.Blocks.Count, "Debugger stepping can audition startup audio.");
        host.Dispose(); host.Dispose();
        Assert.AreEqual(1, audio.Disposals);
        Assert.ThrowsExactly<ObjectDisposedException>(() => host.Resume());
    }

    [TestMethod]
    public void AudioCleanupFailureStillLeavesTheHostDisposedAndIdempotent()
    {
        var audio = new RecordingAudio(throwOnDispose: true);
        var host = new EmulationHost(audio: audio);
        Assert.ThrowsExactly<InvalidOperationException>(() => host.Dispose());
        Assert.AreEqual(EmulationState.Disposed, host.State);
        host.Dispose();
        Assert.AreEqual(1, audio.Disposals);
        Assert.ThrowsExactly<ObjectDisposedException>(() => host.ReleaseAll());
    }

    [TestMethod]
    public void InspectionReturnsDetachedCpuAndMemorySnapshots()
    {
        using var host = Host(0x3E, 0x41, 0xE0, 0x80, 0x3C, 0xE0, 0x80);
        host.Debugging.Step(); host.Debugging.Step();
        CpuSnapshot cpu = host.Inspection.CaptureCpu();
        MemorySnapshot memory = host.Inspection.ReadMemory(0xFF80, 1);
        host.Debugging.Step(); host.Debugging.Step();
        Assert.AreEqual(0x41, cpu.A); Assert.AreEqual(0x41, memory[0]);
        Assert.AreEqual(0x42, host.Inspection.CaptureCpu().A);
        Assert.AreEqual(0x42, host.Inspection.ReadMemory(0xFF80, 1)[0]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => host.Inspection.ReadMemory(0xFFFF, 2));
    }

    [TestMethod]
    public void LoadingOwnsRomBytesAndRejectedCartridgeDoesNotReplaceTheMachine()
    {
        Cartridge rom = Rom(0x3E, 0x41);
        using var host = new EmulationHost(new Gameboy(), options: Options());
        host.Load(rom); rom.Data[0x101] = 0x99;
        host.Debugging.Step(); Assert.AreEqual(0x41, host.Inspection.CaptureCpu().A);
        Cartridge unsupported = Rom(); unsupported.Data[0x147] = 0x19;
        Assert.ThrowsExactly<CartridgeLoadException>(() => host.Load(unsupported));
        Assert.AreEqual(0x41, host.Inspection.CaptureCpu().A);
    }

    [TestMethod]
    public void DebuggerRewindIsBoundedAndRestoresRegistersAndMemory()
    {
        using var host = new EmulationHost(new Gameboy(), options: Options(history: 2));
        host.Load(Rom(0x3E, 0x21, 0x3C, 0xE0, 0x80));
        host.Debugging.Step(); host.Debugging.Step(); host.Debugging.Step();
        Assert.AreEqual(2, host.Debugging.HistoryCount);
        Assert.AreEqual(0x22, host.Inspection.ReadMemory(0xFF80, 1)[0]);
        Assert.IsTrue(host.Debugging.StepBack());
        Assert.AreEqual(0, host.Inspection.ReadMemory(0xFF80, 1)[0]);
        Assert.IsTrue(host.Debugging.StepBack()); Assert.AreEqual(0x21, host.Inspection.CaptureCpu().A);
        Assert.IsFalse(host.Debugging.StepBack());
        host.Resume(); Assert.ThrowsExactly<InvalidOperationException>(() => host.Debugging.Step());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public void FailedDebugStepPreservesPriorHistoryAndThePausedMachine(int historyCapacity)
    {
        using var host = new EmulationHost(new Gameboy(), options: Options(history: historyCapacity));
        host.Load(Rom(0x00, 0xD3));
        host.Debugging.Step();
        CpuSnapshot before = host.Inspection.CaptureCpu();
        long cycles = host.Statistics.Cycles;
        Assert.ThrowsExactly<NotImplementedException>(() => host.Debugging.Step());
        Assert.AreEqual(before, host.Inspection.CaptureCpu());
        Assert.AreEqual(cycles, host.Statistics.Cycles);
        Assert.AreEqual(historyCapacity, host.Debugging.HistoryCount);
        Assert.AreEqual(EmulationState.Paused, host.State);
        if (historyCapacity > 0)
        {
            Assert.IsTrue(host.Debugging.StepBack());
            Assert.AreEqual(0x100, host.Inspection.CaptureCpu().PC);
        }
    }

    [TestMethod]
    public void RewindingStartupAlsoRestoresHostFrameNumbers()
    {
        using var host = new EmulationHost(options: Options());
        host.Load(Rom()); host.Debugging.Step();
        Assert.AreEqual(1L, host.Statistics.Frames);
        Assert.IsTrue(host.Debugging.StepBack());
        Assert.AreEqual(0L, host.Statistics.Frames);
        Assert.AreEqual(0L, host.LatestFrame!.Number);
        Assert.AreEqual(0L, host.Statistics.Cycles);
    }

    [TestMethod]
    public void BreakpointPausesBeforeExecutionAndResumeCanPassTheStoppedInstruction()
    {
        using var host = Host(0x00, 0x00, 0x18, 0xFC);
        host.Debugging.SetBreakpoint(0x101, true);
        host.Resume(); host.Advance(TimeSpan.FromMilliseconds(10));
        Assert.AreEqual(EmulationState.Paused, host.State);
        Assert.AreEqual(0x101, host.Inspection.CaptureCpu().PC);
        long firstStop = host.Statistics.Cycles;
        host.Resume(); host.Advance(TimeSpan.FromMilliseconds(10));
        Assert.AreEqual(EmulationState.Paused, host.State);
        Assert.AreEqual(0x101, host.Inspection.CaptureCpu().PC);
        Assert.IsGreaterThan(firstStop, host.Statistics.Cycles);
    }

    [TestMethod]
    public void RemovingAndReaddingABreakpointDoesNotLeaveAStaleResumeExemption()
    {
        using var host = Host(0xC3, 0x00, 0x01); // JP 0100
        host.Debugging.SetBreakpoint(0x100, true);
        host.Resume(); host.Advance(TimeSpan.FromMilliseconds(1));
        host.Debugging.SetBreakpoint(0x100, false);
        host.Resume(); host.Advance(TimeSpan.FromMilliseconds(1));
        long cycles = host.Statistics.Cycles;
        host.Debugging.SetBreakpoint(0x100, true);
        host.Advance(TimeSpan.FromMilliseconds(1));
        Assert.AreEqual(EmulationState.Paused, host.State);
        Assert.AreEqual(cycles, host.Statistics.Cycles);
    }

    [TestMethod]
    public void ExecutionFaultRequiresResetAndReleasesButtons()
    {
        using var host = Host(0xD3);
        host.Resume(); host.SetButtonState(GameBoyButton.A, true);
        Assert.ThrowsExactly<NotImplementedException>(() => host.Advance(TimeSpan.FromMilliseconds(1)));
        Assert.AreEqual(EmulationState.Faulted, host.State);
        Assert.IsFalse(host.IsPressed(GameBoyButton.A));
        Assert.ThrowsExactly<InvalidOperationException>(() => host.Resume());
        host.Reset(); Assert.AreEqual(EmulationState.Paused, host.State); Assert.IsNull(host.Fault);
    }

    [TestMethod]
    public void FrameCallbacksCannotReenterPlayback()
    {
        using var host = new EmulationHost(options: Options());
        host.Load(Rom()); host.FrameReady += _ => Assert.ThrowsExactly<InvalidOperationException>(() => host.Reset());
        host.Resume(); host.Advance(FrameTime);
        Assert.AreEqual(EmulationState.Running, host.State);
    }

    [TestMethod]
    public async Task LoadingSuspendsPlaybackRejectsOverlapAndDisposalDoesNotResurrectIt()
    {
        using var host = Host(0x18, 0xFE); host.Resume();
        var source = new DelayedSource();
        Task loading = host.LoadAsync(source);
        Assert.IsTrue(host.IsLoading); Assert.AreEqual(EmulationState.Paused, host.State);
        Assert.AreEqual(0L, host.Advance(TimeSpan.FromSeconds(1)));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.LoadAsync(source));
        Assert.ThrowsExactly<InvalidOperationException>(() => host.Reset());
        host.Dispose(); source.Completion.SetResult(Rom());
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => loading);
        Assert.AreEqual(EmulationState.Disposed, host.State);
    }

    [TestMethod]
    public async Task CancelledLoadPreservesThePreviousMachineAndPlaybackRequest()
    {
        using var host = Host(0x18, 0xFE); host.Resume();
        var source = new DelayedSource(); using var cancellation = new CancellationTokenSource();
        Task loading = host.LoadAsync(source, cancellation.Token);
        cancellation.Cancel(); source.Completion.SetResult(Rom(0xD3));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => loading);
        Assert.AreEqual(EmulationState.Running, host.State);
        Assert.IsGreaterThan(0L, host.Advance(TimeSpan.FromMilliseconds(1)));
    }

    [TestMethod]
    public void HeadlessInspectionAndTransferredFramesOwnTheirPixels()
    {
        using var host = new EmulationHost(new Gameboy(), options: Options(captureFrames: false));
        host.Load(Rom(0x18, 0xFE)); host.Resume(); host.Advance(TimeSpan.FromMilliseconds(10));
        Assert.IsNull(host.LatestFrame);
        var original = host.Inspection.CaptureFrame();
        byte[] pixels = original.Pixels.ToArray();
        var transfer = VideoFrame.FromPixels(pixels, original.Number, original.Cycles);
        pixels[0] = (byte)((pixels[0] + 1) & 3);
        CollectionAssert.AreEqual(original.Pixels.ToArray(), transfer.Pixels.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => VideoFrame.FromPixels(new byte[1], 0, 0));
        var invalid = new byte[160 * 144]; invalid[100] = 4;
        Assert.ThrowsExactly<ArgumentException>(() => VideoFrame.FromPixels(invalid, 0, 0));
    }

    private sealed class DelayedSource : ICartridgeSource
    {
        public string Name => "test-cartridge";
        public TaskCompletionSource<Cartridge> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Cartridge> LoadAsync(CancellationToken cancellationToken) => this.Completion.Task;
    }
    private sealed class RecordingAudio(bool throwOnDispose = false) : IAudioOutput
    {
        public List<short[]> Blocks { get; } = new();
        public int Flushes { get; private set; }
        public int Disposals { get; private set; }
        public void Write(ReadOnlyMemory<short> samples) => this.Blocks.Add(samples.ToArray());
        public void Flush() => this.Flushes++;
        public void Dispose()
        {
            this.Disposals++;
            if (throwOnDispose) throw new InvalidOperationException("Test audio cleanup failure.");
        }
    }
}
