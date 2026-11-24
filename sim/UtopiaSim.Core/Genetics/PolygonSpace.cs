namespace UtopiaSim;

/// <summary>
/// Polígono regular de N lados inscrito en el círculo de radio 1.
/// N = 2 es una línea de (-1,0) a (1,0); N = 3 triángulo; N = 4 cuadrado; con N grande se acerca al círculo.
/// Las esquinas son "especialistas": con <see cref="BoundaryRule.Clamp"/>, 1/N de las mutaciones
/// que salen de una esquina vuelven a ella (menos lados = esquinas más pegajosas).
/// </summary>
public sealed class PolygonSpace : TraitSpace
{
    readonly Vec2[] v;

    public PolygonSpace(int sides, BoundaryRule boundary) : base(boundary)
    {
        if (sides < 2)
            throw new ArgumentOutOfRangeException(nameof(sides), "Un polígono necesita al menos 2 lados (2 = línea).");
        Sides = sides;
        v = new Vec2[sides];
        if (sides == 2)
        {
            v[0] = new Vec2(-1, 0);
            v[1] = new Vec2(1, 0);
        }
        else
        {
            // Vértices en sentido antihorario. Con N par se rota para que haya un lado abajo (cuadrado alineado).
            double start = Math.PI / 2 + (sides % 2 == 0 ? Math.PI / sides : 0);
            for (int i = 0; i < sides; i++) v[i] = Vec2.FromAngle(start + i * MathUtil.Tau / sides);
        }
    }

    public int Sides { get; }

    public IReadOnlyList<Vec2> Vertices => v;

    bool IsLine => Sides == 2;

    public override bool Contains(Vec2 p)
    {
        if (IsLine) return Math.Abs(p.Y) <= Eps && p.X >= -1 - Eps && p.X <= 1 + Eps;
        for (int i = 0; i < Sides; i++)
        {
            Vec2 a = v[i], b = v[(i + 1) % Sides];
            if (Vec2.Cross(b - a, p - a) < -Eps) return false;
        }
        return true;
    }

    public override Vec2 Project(Vec2 p)
    {
        if (IsLine) return new Vec2(MathUtil.Clamp(p.X, -1, 1), 0);
        if (Contains(p)) return p;
        Vec2 best = p;
        double bestDist = double.MaxValue;
        for (int i = 0; i < Sides; i++)
        {
            Vec2 q = ClosestOnSegment(p, v[i], v[(i + 1) % Sides]);
            double d = (q - p).LengthSquared;
            if (d < bestDist)
            {
                bestDist = d;
                best = q;
            }
        }
        return best;
    }

    protected override Vec2 ReflectOnce(Vec2 p)
    {
        if (IsLine)
        {
            double x = p.X;
            if (x > 1) x = 2 - x;
            else if (x < -1) x = -2 - x;
            return new Vec2(x, 0);
        }

        // Reflejar contra el lado más violado. La normal apunta hacia adentro.
        double worst = 0;
        Vec2 worstNormal = default;
        for (int i = 0; i < Sides; i++)
        {
            Vec2 a = v[i], e = v[(i + 1) % Sides] - a;
            Vec2 n = new Vec2(-e.Y, e.X) / e.Length;
            double s = Vec2.Dot(p - a, n);
            if (s < worst)
            {
                worst = s;
                worstNormal = n;
            }
        }
        return worst < 0 ? p - worstNormal * (2 * worst) : p;
    }

    protected override Vec2 RandomDirection(Rng rng) =>
        IsLine ? new Vec2(rng.NextDouble() < 0.5 ? -1 : 1, 0) : Vec2.FromAngle(rng.Angle());

    /// <summary>Solo para la línea: 0 en el vértice 0 (izquierda), 1 en el vértice 1 (derecha).</summary>
    public double LinePosition(Vec2 p) => MathUtil.Clamp((p.X + 1) / 2, 0, 1);

    /// <summary>
    /// Cuánto "pertenece" un punto a cada esquina: pesos ≥ 0 que suman 1, y Σ peso·vértice = punto.
    /// En una esquina vale 1 para esa esquina. Sirve para leer un gen como mezcla de especialistas.
    /// (Triángulo: coordenadas baricéntricas. N ≥ 4: coordenadas de Wachspress.)
    /// </summary>
    public double[] VertexWeights(Vec2 p)
    {
        var w = new double[Sides];
        VertexWeights(p, w);
        return w;
    }

    public void VertexWeights(Vec2 p, Span<double> w)
    {
        if (w.Length != Sides) throw new ArgumentException($"Se esperaban {Sides} pesos.", nameof(w));
        w.Clear();
        p = Project(p);

        if (IsLine)
        {
            double t = LinePosition(p);
            w[0] = 1 - t;
            w[1] = t;
            return;
        }

        for (int i = 0; i < Sides; i++)
        {
            if ((p - v[i]).LengthSquared < 1e-18)
            {
                w[i] = 1;
                return;
            }
        }

        // Sobre un lado: interpolación lineal entre sus dos vértices.
        for (int i = 0; i < Sides; i++)
        {
            int j = (i + 1) % Sides;
            Vec2 e = v[j] - v[i];
            if (Vec2.Cross(e, p - v[i]) <= 1e-12)
            {
                double t = MathUtil.Clamp(Vec2.Dot(p - v[i], e) / e.LengthSquared, 0, 1);
                w[i] = 1 - t;
                w[j] = t;
                return;
            }
        }

        // Interior: Wachspress. w_i ∝ área(v_{i-1}, v_i, v_{i+1}) / (área(p, v_{i-1}, v_i) · área(p, v_i, v_{i+1}))
        double sum = 0;
        for (int i = 0; i < Sides; i++)
        {
            Vec2 prev = v[(i - 1 + Sides) % Sides], cur = v[i], next = v[(i + 1) % Sides];
            double areaPrev = Vec2.Cross(cur - prev, p - prev);
            double areaNext = Vec2.Cross(next - cur, p - cur);
            double corner = Vec2.Cross(cur - prev, next - prev);
            w[i] = corner / (areaPrev * areaNext);
            sum += w[i];
        }
        for (int i = 0; i < Sides; i++) w[i] /= sum;
    }

    static Vec2 ClosestOnSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        Vec2 e = b - a;
        double t = MathUtil.Clamp(Vec2.Dot(p - a, e) / e.LengthSquared, 0, 1);
        return a + e * t;
    }
}
