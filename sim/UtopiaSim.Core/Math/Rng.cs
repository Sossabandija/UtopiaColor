using System.Numerics;

namespace UtopiaSim;

/// <summary>
/// Generador de números aleatorios con semilla (xoshiro256**).
/// Implementación propia para que una semilla dé exactamente la misma corrida
/// en cualquier máquina y versión de .NET: así los experimentos son reproducibles.
/// </summary>
public sealed class Rng
{
    ulong s0, s1, s2, s3;

    public Rng(ulong seed)
    {
        ulong x = seed;
        s0 = SplitMix64(ref x);
        s1 = SplitMix64(ref x);
        s2 = SplitMix64(ref x);
        s3 = SplitMix64(ref x);
    }

    static ulong SplitMix64(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextULong()
    {
        ulong result = BitOperations.RotateLeft(s1 * 5, 7) * 9;
        ulong t = s1 << 17;
        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;
        s2 ^= t;
        s3 = BitOperations.RotateLeft(s3, 45);
        return result;
    }

    /// <summary>Número en [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Número en [min, max).</summary>
    public double Range(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>Entero en [0, n).</summary>
    public int Below(int n) => (int)(NextDouble() * n);

    /// <summary>Ángulo en [0, 2π).</summary>
    public double Angle() => NextDouble() * MathUtil.Tau;
}
