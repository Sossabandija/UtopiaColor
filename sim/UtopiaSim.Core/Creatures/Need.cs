namespace UtopiaSim;

/// <summary>
/// Necesidades de un animal. Cada una va de 1 (satisfecha) a 0 (urgente);
/// en cada tick el animal atiende la más baja.
/// La de comida es además su peso: es lo que entrega al ser comido.
/// </summary>
public static class Need
{
    public const int Food = 0;
    public const int Water = 1;
    public const int Mate = 2;
    public const int Rest = 3;
    public const int Safety = 4;
    public const int Care = 5;
    public const int Count = 6;

    public static readonly string[] Names = ["hambre", "sed", "pareja", "sueño", "huir", "cuidar crías"];
}

/// <summary>Lo que un animal "aprieta" este tick (el equivalente a las teclas del original).</summary>
[Flags]
public enum Intent
{
    None = 0,
    Forward = 1,
    Back = 2,
    /// <summary>Moverse de lado, hacia ángulo − 90°.</summary>
    StrafeNeg = 4,
    /// <summary>Moverse de lado, hacia ángulo + 90°.</summary>
    StrafePos = 8,
    Jump = 16,
    /// <summary>Girar disminuyendo el ángulo.</summary>
    TurnNeg = 32,
    /// <summary>Girar aumentando el ángulo.</summary>
    TurnPos = 64,
    Wander = 128,
}

/// <summary>Refuge: un punto de su zona de refugio, hacia donde huye.</summary>
public enum TargetKind { None, Creature, Water, Refuge }

/// <summary>
/// Starved hambre, Dehydrated sed, Eaten comido, Crowded semilla que cayó en una casilla llena,
/// Seedling brote que murió antes de establecerse (ver <see cref="SeedlingConfig"/>).
/// </summary>
public enum DeathCause { Starved, Dehydrated, Eaten, Crowded, Seedling }

/// <summary>Qué come un animal de una presa: nada, la presa entera (la mata) o un fruto (la planta sigue viva).</summary>
public enum MealKind { None, Whole, Fruit }
