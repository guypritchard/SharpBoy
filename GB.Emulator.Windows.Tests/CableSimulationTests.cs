using System.Runtime.ExceptionServices;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using GB.Emulator.Windows.Desktop;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Windows.Tests;

[TestClass]
public class CableSimulationTests
{
    [TestMethod]
    public void CordKeepsBothEndsAndLengthAndSettlesUnderGravity()
    {
        var rope = new CableSimulation();
        Point3D start = new(0, 30, 0), end = new(90, 0, 0);
        for (int i = 0; i < 600; i++) rope.Advance(1d / 120, start, new(1,0,0), end, new(-1,0,0));
        Assert.AreEqual(start, rope.Points[0]); Assert.AreEqual(end, rope.Points[^1]);
        Assert.IsLessThan(CableSimulation.Length * 1.04, rope.ActualLength);
        Assert.IsGreaterThan(-20d, rope.Points.Min(p => p.Y), "The jacket should support the cord instead of dropping into a deep U.");
        var settled = rope.Points.ToArray();
        for (int i = 0; i < 120; i++) rope.Advance(1d / 120, start, new(1,0,0), end, new(-1,0,0));
        Assert.IsLessThan(1d, settled.Select((p,i) => (p - rope.Points[i]).Length).Max(), "Damping should settle the cable.");
        for (int i = 0; i < 1200 && !rope.IsSleeping; i++) rope.Advance(1d / 120, start, new(1,0,0), end, new(-1,0,0));
        Assert.IsTrue(rope.IsSleeping, "An idle cord should stop consuming simulation/render work.");
        rope.Advance(0, start + new Vector3D(3, 0, 0), new(1,0,0), end, new(-1,0,0));
        Assert.IsFalse(rope.IsSleeping, "Moving either plug wakes the cord.");
    }

    [TestMethod]
    public void BendStiffnessReducesSettledDroopWithIdenticalLengthGravityAndDamping()
    {
        var soft = new CableSimulation(bendingStiffness: 0);
        var stiff = new CableSimulation(bendingStiffness: .8);
        Point3D start = new(0, 0, 0), end = new(100, 0, 0);
        for (int i = 0; i < 1800; i++)
        {
            soft.Advance(1d / 120, start, new(1,0,0), end, new(-1,0,0));
            stiff.Advance(1d / 120, start, new(1,0,0), end, new(-1,0,0));
        }
        double softDroop = -soft.Points.Min(p => p.Y), stiffDroop = -stiff.Points.Min(p => p.Y);
        Console.WriteLine($"Settled droop with equal gravity, damping and length: flexible={softDroop:F2}, stiff={stiffDroop:F2} model units.");
        Assert.IsLessThan(softDroop * .5, stiffDroop, $"Stiffness must change the equilibrium shape: soft={softDroop:F2}, stiff={stiffDroop:F2}.");
        Assert.IsLessThan(20d, stiffDroop);
        Assert.IsLessThan(CableSimulation.Length * 1.04, stiff.ActualLength);
        Assert.AreEqual(start, stiff.Points[0]); Assert.AreEqual(end, stiff.Points[^1]);
    }

    [TestMethod]
    public void MovingAPlugDoesNotLeaveTheCordSwingingForSeveralSeconds()
    {
        var rope = new CableSimulation();
        Point3D end = new(100, 0, 0);
        void Step() => rope.Advance(1d / 120, new(), new(1,0,0), end, new(-1,0,0));
        for (int i = 0; i < 1200; i++) Step();
        for (int i = 0; i < 12; i++) { end.X += 2; Step(); }
        for (int i = 0; i < 360; i++) Step();
        var settled = rope.Points.ToArray();
        for (int i = 0; i < 120; i++) Step();
        Assert.IsLessThan(1d, settled.Select((p,i) => (p - rope.Points[i]).Length).Max(), "A quick drag should settle without a long floppy swing.");
        Assert.AreEqual(end, rope.Points[^1]);
        Assert.IsLessThan(CableSimulation.Length * 1.04, rope.ActualLength);
    }

    [TestMethod]
    public void FixedStepIsIndependentOfRenderFrequencyAndHandlesLongPauses()
    {
        var a = new CableSimulation(); var b = new CableSimulation();
        for (int i = 0; i < 240; i++) a.Advance(1d / 60, new(), new(1,0,0), new(100,0,0), new(-1,0,0));
        for (int i = 0; i < 480; i++) b.Advance(1d / 120, new(), new(1,0,0), new(100,0,0), new(-1,0,0));
        for (int i = 0; i < a.Points.Count; i++) Assert.IsLessThan(.000001, (a.Points[i] - b.Points[i]).Length);
        a.Advance(300, new(), new(1,0,0), new(100,0,0), new(-1,0,0));
        Assert.IsTrue(a.Points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)));
        Assert.IsLessThan(CableSimulation.Length * 1.04, a.ActualLength);
    }

    [TestMethod]
    public void DesktopObjectsMoveIndependentlyWithPinnedPlugsAndFiniteCableReach()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            DesktopWindow? window = null;
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    window = new DesktopWindow([], animateViewTransitions: true); window.Show();
                    window.OpenLinkLobby(); window.LinkLobby!.Hide();
                    await Task.Delay(100);
                    var scene = window.Scene; var cable = scene.Cable!;
                    for (int i = 0; i < 240; i++) scene.AdvanceCable(1d / 120);
                    string folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/link-cable"));
                    window.Capture(Path.Combine(folder, "resting.png"));
                    var originalHub = scene.HubPosition;
                    // On a small CI desktop the hardware legitimately fills most of the screen.
                    Assert.AreEqual(scene.RenderCrop.Width * scene.RenderCrop.Height, scene.View.ActualWidth * scene.View.ActualHeight, .01);
                    Assert.IsLessThanOrEqualTo(window.Width * window.Height, scene.RenderCrop.Width * scene.RenderCrop.Height);
                    System.Windows.Point DesktopPoint(Point3D world)
                    { window.UpdateLayout(); return scene.View.TranslatePoint(scene.ProjectWorld(world), window); }
                    var beforeCrop = DesktopPoint(scene.ConsoleSocket);
                    scene.MoveLinkedObject(true, new(30,0,0));
                    var afterCrop = DesktopPoint(scene.ConsoleSocket);
                    Assert.IsLessThan(.01, (afterCrop - beforeCrop).Length, "Changing the render crop must not shift the stationary Game Boy.");
                    var lcd = scene.ScreenBounds;
                    Assert.IsTrue(scene.HitScreen(scene.Project(new(lcd.X + lcd.SizeX/2, lcd.Y + lcd.SizeY/2, lcd.Z + lcd.SizeZ + .2))),
                        "The off-centre frustum must preserve LCD hit testing.");
                    scene.MoveLinkedObject(true, new(-30,0,0));
                    // Workspace/cable limits can clamp either drag, so an opposite
                    // drag need not return to the exact initial position.
                    originalHub = scene.HubPosition;
                    scene.SetView(55, 12);
                    Assert.AreEqual(originalHub, scene.HubPosition, "Rotating the Game Boy must not rotate its hub.");
                    Assert.AreEqual(scene.ConsoleSocket, cable.ConsolePlug.Transform.Transform(LinkCableModel.PortAnchor));
                    scene.SetView(-14, -5);
                    scene.MoveLinkedObject(false, new(-35, 10, 0));
                    Assert.AreEqual(originalHub, scene.HubPosition);
                    Assert.IsGreaterThan(1d, scene.ConsoleOffset.Length);
                    var console = scene.ConsoleOffset;
                    scene.MoveLinkedObject(true, new(35, 12, 0));
                    Assert.AreEqual(console, scene.ConsoleOffset);
                    Assert.AreNotEqual(originalHub, scene.HubPosition);
                    for (int i = 0; i < 240; i++) scene.AdvanceCable(1d / 120);
                    window.Capture(Path.Combine(folder, "stretched.png"));
                    // An extreme request must be clamped; it cannot turn the wire into an infinite rubber band.
                    scene.MoveLinkedObject(true, new(10000, 0, 0));
                    for (int i = 0; i < 240; i++) scene.AdvanceCable(1d / 120);
                    Assert.IsLessThanOrEqualTo(CableSimulation.Length * 1.06, cable.Simulation.ActualLength);
                    Assert.AreEqual(cable.ConsolePlug.Transform.Transform(LinkCableModel.ConsoleTail), cable.Start);
                    Assert.AreEqual(cable.HubAssembly.Transform.Transform(LinkCableModel.HubTail), cable.End);
                    Assert.AreEqual(cable.Start, cable.Simulation.Points[0]); Assert.AreEqual(cable.End, cable.Simulation.Points[^1]);
                    window.Capture(Path.Combine(folder, "taut.png"));
                    Assert.IsTrue(scene.HitHub(scene.ProjectWorld(scene.HubPosition + new Vector3D(0,0,9))));
                    foreach (double yaw in new[] { 80d, 170d, 250d, 350d })
                    {
                        var hubBefore = scene.HubPosition;
                        scene.SetView(yaw, 10);
                        for (int i = 0; i < 360; i++) scene.AdvanceCable(1d / 120);
                        Assert.AreEqual(hubBefore, scene.HubPosition);
                        Assert.IsLessThanOrEqualTo(CableSimulation.Length * 1.08, cable.Simulation.ActualLength, $"Extended cable at yaw {yaw}");
                        Assert.IsTrue(cable.Simulation.Points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)));
                        window.Capture(Path.Combine(folder, $"rotated-{yaw}.png"));
                    }
                    console = scene.ConsoleOffset;
                    async Task WaitForTransition()
                    {
                        var deadline = System.Diagnostics.Stopwatch.StartNew();
                        while (window.IsViewTransitioning && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
                        Assert.IsFalse(window.IsViewTransitioning, "The bounded view transition must finish even on software-rendered CI desktops.");
                    }
                    window.ToggleScreenCloseUp(); await WaitForTransition();
                    Assert.IsTrue(window.IsScreenCloseUp); Assert.IsFalse(window.IsViewTransitioning);
                    window.ToggleScreenCloseUp(); await WaitForTransition();
                    Assert.IsFalse(window.IsScreenCloseUp); Assert.IsFalse(window.IsViewTransitioning);
                    Assert.AreEqual(console, scene.ConsoleOffset, "Close-up should preserve the physical layout.");
                }
                catch (Exception ex) { failure = ex; }
                finally { window?.Close(); dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(40)), "Cable test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
