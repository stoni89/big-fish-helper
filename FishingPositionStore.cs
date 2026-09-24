using System;
using System.Numerics;

namespace BigFishHelper;

public enum FishingPositionSource
{
    // Per "Position speichern"-Knopf (nur Dev-Version) gespeichert.
    Saved,
    // Fest im Code hinterlegt (BigFishData.FishingPositions).
    Builtin,
}

public readonly record struct FishingPosition(Vector3 Position, float? Facing, FishingPositionSource Source);

/// <summary>Gespeicherte Angel-Position (Konfiguration) - Position + Blickrichtung (FFXIV-Rotation).</summary>
[Serializable]
public class SavedFishingPosition
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float? Facing { get; set; }

    public Vector3 ToVector3() => new(X, Y, Z);

    public static SavedFishingPosition From(Vector3 position, float? facing) =>
        new() { X = position.X, Y = position.Y, Z = position.Z, Facing = facing };
}

/// <summary>
/// Liefert die Angel-Position eines Big Fish - Vorrang: per Dev-Knopf gespeichert, dann im Code
/// hinterlegt. Null = keine Position eingetragen (die Automation überspringt den Fisch).
/// </summary>
public static class FishingPositionStore
{
    public static FishingPosition? Get(Configuration config, uint itemId)
    {
        if (config.SavedFishingPositions.TryGetValue(itemId, out var saved))
            return new FishingPosition(saved.ToVector3(), saved.Facing, FishingPositionSource.Saved);

        if (BigFishData.FishingPositions.TryGetValue(itemId, out var builtin))
            return new FishingPosition(builtin, BigFishData.FishingFacings.TryGetValue(itemId, out var facing) ? facing : null, FishingPositionSource.Builtin);

        return null;
    }

    /// <summary>C#-Zeilen zum Übernehmen in BigFishData (Position + Blickrichtung).</summary>
    public static string ToCodeLines(uint itemId, string fishName, Vector3 position, float facing) =>
        FormattableString.Invariant(
            $"[{itemId}] = new Vector3({position.X}f, {position.Y}f, {position.Z}f), // {fishName}\n[{itemId}] = {facing}f, // {fishName} (Blickrichtung)");
}
