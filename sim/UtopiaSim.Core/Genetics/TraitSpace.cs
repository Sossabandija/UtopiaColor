namespace UtopiaSim;

/// <summary>Qué hacer cuando una mutación cae fuera de la forma del gen.</summary>
public enum BoundaryRule
{
    /// <summary>
    /// Se sortea otra mutación hasta caer dentro (así funciona el original).
    /// Efecto: los bordes y esquinas empujan hacia adentro.
    /// </summary>
    Reject,

    /// <summary>La mutación rebota en el borde como en una pared. Efecto: neutral.</summary>
    Reflect,

    /// <summary>
    /// La mutación queda en el punto del borde más cercano.
    /// Efecto: bordes y esquinas "pegajosos". Desde una esquina de un polígono de N lados,
    /// 1/N de las mutaciones vuelve exactamente a esa esquina.
    /// </summary>
    Clamp,
}

/// <summary>
/// La forma del espacio donde vive un gen. El valor de un gen es un punto dentro de la forma.
/// La forma decide cómo se mueven las mutaciones; qué significa cada posición lo decide quien lee el gen.
/// </summary>
public abstract class TraitSpace
{
    protected const double Eps = 1e-9;
    const int MaxRejectAttempts = 1000;
    const int MaxReflections = 16;

    protected TraitSpace(BoundaryRule boundary) => Boundary = boundary;

    public BoundaryRule Boundary { get; }

    public abstract bool Contains(Vec2 p);

    /// <summary>El punto de la forma más cercano a <paramref name="p"/> (p mismo si ya está dentro).</summary>
    public abstract Vec2 Project(Vec2 p);

    /// <summary>Refleja un punto de afuera contra el borde que cruzó.</summary>
    protected abstract Vec2 ReflectOnce(Vec2 p);

    /// <summary>Dirección unitaria al azar en la que puede moverse una mutación.</summary>
    protected abstract Vec2 RandomDirection(Rng rng);

    /// <summary>Promedio de dos padres. Las formas son convexas, así que el promedio siempre queda dentro.</summary>
    public virtual Vec2 Blend(Vec2 a, Vec2 b) => Project((a + b) * 0.5);

    public double Distance(Vec2 a, Vec2 b) => Vec2.Distance(a, b);

    /// <summary>
    /// Muta un punto: salto en dirección al azar, de largo <c>maxStep * U^exponent</c> (U uniforme en [0,1)).
    /// Un exponente alto hace que casi todos los saltos sean chicos y unos pocos grandes.
    /// </summary>
    public Vec2 Mutate(Vec2 p, Rng rng, double maxStep, double exponent)
    {
        if (maxStep <= 0) return p;

        if (Boundary == BoundaryRule.Reject)
        {
            for (int i = 0; i < MaxRejectAttempts; i++)
            {
                Vec2 q = p + RandomDirection(rng) * StepLength(rng, maxStep, exponent);
                if (Contains(q)) return q;
            }
            return Project(p);
        }

        Vec2 r = p + RandomDirection(rng) * StepLength(rng, maxStep, exponent);
        if (Contains(r)) return r;
        if (Boundary == BoundaryRule.Reflect)
        {
            for (int i = 0; i < MaxReflections && !Contains(r); i++) r = ReflectOnce(r);
        }
        return Project(r);
    }

    static double StepLength(Rng rng, double maxStep, double exponent) =>
        maxStep * Math.Pow(rng.NextDouble(), exponent);

    public static TraitSpace Create(GeneConfig cfg) => cfg.Shape switch
    {
        GeneShape.Disk => new DiskSpace(cfg.Boundary),
        GeneShape.Polygon => new PolygonSpace(cfg.Sides, cfg.Boundary),
        _ => throw new ArgumentOutOfRangeException(nameof(cfg), $"Forma de gen desconocida: {cfg.Shape}"),
    };
}
