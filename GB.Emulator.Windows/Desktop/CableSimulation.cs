using System.Windows.Media.Media3D;

namespace GB.Emulator.Windows.Desktop;

/// <summary>A fixed-length, damped cord. End particles follow the two strain reliefs;
/// distance and bend constraints keep the remaining particles flexible but inextensible.</summary>
internal sealed class CableSimulation
{
    private const double Step = 1d / 120;
    private const double VelocityRetention = .985;
    private const double Gravity = 300;
    private static readonly int[] BendSpans = [1, 4, 8];
    private readonly Point3D[] points = new Point3D[49], previous = new Point3D[49];
    private readonly Point3D[] rest = new Point3D[49];
    private readonly Point3D[] curveBases = new Point3D[129], curveSamples = new Point3D[129];
    private readonly double[] curveDistances = new double[129];
    private static readonly double[] CurveBulge = Enumerable.Range(0, 129).Select(i => Math.Pow(Math.Sin(i * Math.PI / 128), 2)).ToArray();
    private readonly double bendingStiffness;
    private double accumulator;
    private bool initialized;
    private Point3D lastStart, lastEnd;
    private Vector3D lastStartOut, lastEndOut;
    private int quietSteps;
    internal bool IsSleeping { get; private set; }
    internal const double Length = 175;
    internal double Spacing => Length / (points.Length - 1);
    internal IReadOnlyList<Point3D> Points => points;
    internal double MaximumInnerSpan => (Length - 2 * Spacing) * .98;
    internal double ActualLength => Enumerable.Range(1, points.Length - 1).Sum(i => (points[i] - points[i - 1]).Length);
    internal double Tension { get; private set; }

    internal CableSimulation(double bendingStiffness = .8)
    {
        if (!double.IsFinite(bendingStiffness) || bendingStiffness is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(bendingStiffness));
        this.bendingStiffness = bendingStiffness;
    }

    internal void Advance(double seconds, Point3D start, Vector3D startOut, Point3D end, Vector3D endOut,
        Func<Point3D, Point3D>? excludeSolids = null)
    {
        startOut.Normalize(); endOut.Normalize();
        if (start != lastStart || end != lastEnd || startOut != lastStartOut || endOut != lastEndOut)
        { IsSleeping = false; quietSteps = 0; BuildRestCurve(start, startOut, end, endOut); }
        lastStart = start; lastEnd = end; lastStartOut = startOut; lastEndOut = endOut;
        var startInner = start + startOut * Spacing;
        var endInner = end + endOut * Spacing;
        Tension = Math.Clamp((endInner - startInner).Length / MaximumInnerSpan, 0, 1);
        if (IsSleeping) { accumulator = 0; return; }
        // A housing can obstruct the free rest curve. Let that bend yield instead of
        // pulling the cord through solid geometry or stretching its length constraints.
        double effectiveBending = bendingStiffness;
        if (excludeSolids != null)
            foreach (var point in rest)
                if ((excludeSolids(point) - point).LengthSquared > .01) { effectiveBending *= .1; break; }
        if (!initialized)
        {
            Array.Copy(rest, points, points.Length);
            initialized = true;
            Pin();
            Solve(100);
            Array.Copy(points, previous, points.Length);
        }
        accumulator += Math.Clamp(double.IsFinite(seconds) ? seconds : 0, 0, .05);
        // Pin even on a zero-time update: neither plug can detach while the mouse moves.
        Pin();
        while (accumulator >= Step)
        {
            accumulator -= Step;
            for (int i = 2; i < points.Length - 2; i++)
            {
                Point3D current = points[i];
                Vector3D velocity = (current - previous[i]) * VelocityRetention;
                if (velocity.Length > 5) velocity *= 5 / velocity.Length;
                points[i] += velocity + new Vector3D(0, -Gravity * Step * Step, 0);
                previous[i] = current;
            }
            Solve(32);
            double motion = 0;
            for (int i = 2; i < points.Length - 2; i++) motion = Math.Max(motion, (points[i] - previous[i]).LengthSquared);
            quietSteps = motion < .00001 ? quietSteps + 1 : 0;
            if (quietSteps >= 120) { IsSleeping = true; accumulator = 0; break; }
        }

        void Pin()
        {
            points[0] = start; points[1] = startInner;
            points[^2] = endInner; points[^1] = end;
        }
        bool Free(int i) => i >= 2 && i < points.Length - 2;
        void Distance(int a, int b, double length, bool minimum = false)
        {
            var delta = points[b] - points[a]; double distance = delta.Length;
            if (distance < .00001 || minimum && distance >= length) return;
            double wa = Free(a) ? 1 : 0, wb = Free(b) ? 1 : 0;
            if (wa + wb == 0) return;
            var correction = delta * ((distance - length) / (distance * (wa + wb)));
            points[a] += correction * wa; points[b] -= correction * wb;
        }
        void Solve(int iterations)
        {
            for (int pass = 0; pass < iterations; pass++)
            {
                for (int i = 0; i < points.Length - 2; i++) Distance(i, i + 2, Spacing * 1.65, minimum: true);
                // Curvature memory gives the jacket actual static flexural stiffness.
                // Several spans support a broad arch against gravity; damping only removes oscillation.
                if (effectiveBending > 0)
                    foreach (int span in BendSpans)
                        for (int i = span; i < points.Length - span; i++) Bend(i - span, i, i + span);
                for (int i = 0; i < points.Length - 1; i++) Distance(i, i + 1, Spacing);
                for (int i = points.Length - 2; i >= 0; i--) Distance(i, i + 1, Spacing);
                if (excludeSolids != null)
                    for (int i = 2; i < points.Length - 2; i++) points[i] = excludeSolids(points[i]);
                Pin();
            }
        }
        void Bend(int a, int b, int c)
        {
            var error = ((Vector3D)points[a] - 2 * (Vector3D)points[b] + (Vector3D)points[c]) -
                ((Vector3D)rest[a] - 2 * (Vector3D)rest[b] + (Vector3D)rest[c]);
            double wa = Free(a) ? 1 : 0, wb = Free(b) ? 1 : 0, wc = Free(c) ? 1 : 0;
            double weight = wa + 4 * wb + wc;
            if (weight == 0) return;
            var correction = error * (effectiveBending / weight);
            points[a] -= correction * wa; points[b] += correction * (2 * wb); points[c] -= correction * wc;
        }
    }

    private void BuildRestCurve(Point3D start, Vector3D startOut, Point3D end, Vector3D endOut)
    {
        // A sheathed link lead retains a broad bend in depth instead of collapsing into
        // a hanging string. Fit its full material length, so stiffness never hides slack by shortening it.
        const int samples = 128;
        var bases = curveBases; var bulge = CurveBulge;
        var curve = curveSamples; var distances = curveDistances;
        var chord = end - start; double distance = chord.Length;
        var support = new Vector3D(0, 0, 1);
        if (distance > .00001)
        {
            var direction = chord / distance;
            support -= direction * Vector3D.DotProduct(support, direction);
            if (support.LengthSquared < .01) support = new(1,0,0);
        }
        support.Normalize();
        double handle = Math.Min(Length * .3, Math.Max(Spacing * 2, (Length - distance) * .4));
        var a = start + startOut * handle; var b = end + endOut * handle;
        for (int i = 0; i <= samples; i++)
        {
            double t = (double)i / samples, u = 1 - t;
            bases[i] = (Point3D)((Vector3D)start * (u*u*u) + (Vector3D)a * (3*u*u*t) +
                (Vector3D)b * (3*u*t*t) + (Vector3D)end * (t*t*t));
        }
        double Measure(double amplitude)
        {
            distances[0] = 0;
            for (int i = 0; i <= samples; i++)
            {
                curve[i] = bases[i] + support * (bulge[i] * amplitude);
                if (i > 0) distances[i] = distances[i - 1] + (curve[i] - curve[i - 1]).Length;
            }
            return distances[^1];
        }
        double low = 0, high = Length;
        for (int i = 0; i < 16; i++)
        {
            double middle = (low + high) / 2;
            if (Measure(middle) < Length) low = middle; else high = middle;
        }
        double total = Measure((low + high) / 2); int at = 1;
        for (int i = 0; i < rest.Length; i++)
        {
            double target = total * i / (rest.Length - 1);
            while (at < samples && distances[at] < target) at++;
            double fraction = (target - distances[at - 1]) / Math.Max(.00001, distances[at] - distances[at - 1]);
            rest[i] = curve[at - 1] + (curve[at] - curve[at - 1]) * fraction;
        }
    }

    internal static Point3D OutsideBox(Point3D p, Rect3D box)
    {
        if (!box.Contains(p)) return p;
        ReadOnlySpan<double> distances = stackalloc double[] { p.X - box.X, box.X + box.SizeX - p.X, p.Y - box.Y,
            box.Y + box.SizeY - p.Y, p.Z - box.Z, box.Z + box.SizeZ - p.Z };
        int side = 0;
        for (int i = 1; i < distances.Length; i++) if (distances[i] < distances[side]) side = i;
        return side switch
        {
            0 => new(box.X - .01, p.Y, p.Z), 1 => new(box.X + box.SizeX + .01, p.Y, p.Z),
            2 => new(p.X, box.Y - .01, p.Z), 3 => new(p.X, box.Y + box.SizeY + .01, p.Z),
            4 => new(p.X, p.Y, box.Z - .01), _ => new(p.X, p.Y, box.Z + box.SizeZ + .01)
        };
    }
}
