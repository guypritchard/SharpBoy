using System.Runtime.ExceptionServices;
using System.Windows.Media.Media3D;
using GB.Emulator.Windows.Desktop;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Windows.Tests;

[TestClass]
public class CableRenderingPerformanceTests
{
    [TestMethod]
    public void SleepingCordReusesItsMeshAndMovingCordDoesNotAllocateFreshVertexBuffers() => Sta(() =>
    {
        var model = new LinkCableModel(); var rotation = new AxisAngleRotation3D(new(0,1,0), 0);
        var transform = new RotateTransform3D(rotation); var hub = new Vector3D(35,5,0);
        for (int i = 0; i < 1800 && !model.Simulation.IsSleeping; i++) model.Update(1d / 120, transform, hub);
        Assert.IsTrue(model.Simulation.IsSleeping);
        long revisions = model.MeshUpdates, before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) model.Update(1d / 60, transform, hub);
        Assert.AreEqual(revisions, model.MeshUpdates, "Idle presentation must not invalidate the 3D scene.");
        Assert.IsLessThan(16_384L, GC.GetAllocatedBytesForCurrentThread() - before);
        for (int i = 0; i < 20; i++) { rotation.Angle = Math.Sin(i * .1) * 12; model.Update(1d / 60, transform, hub); }
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) { rotation.Angle = Math.Sin(i * .1) * 12; model.Update(1d / 60, transform, hub); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"600 moving cable updates allocated {allocated:N0} bytes.");
        Assert.IsLessThan(8_000_000L, allocated, "Moving the cable must reuse its vertex, normal and curve buffers.");
    });
    private static void Sta(Action check)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { check(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
