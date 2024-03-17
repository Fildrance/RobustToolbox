using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Robust.Shared.Collections;
using Robust.Shared.Maths;

namespace Robust.Shared.Random;

/// <summary>
/// Wrapper around random number generator helping methods.
/// </summary>
public interface IRobustRandom
{
    /// <summary> Get the underlying <see cref="Random"/>.</summary>
    System.Random GetRandom();

    /// <summary> Set seed for underlying <see cref="Random"/>. </summary>
    void SetSeed(int seed);

    /// <summary> Get random <see cref="float"/> value. </summary>
    float NextFloat();

    /// <summary> Get random <see cref="float"/> value in range of <paramref name="minValue"/> and <paramref name="maxValue"/>. </summary>
    /// <param name="minValue">Random value should be greater or equal to this value.</param>
    /// <param name="maxValue">Random value should be less or equal to this value.</param>
    public float NextFloat(float minValue, float maxValue)
        => NextFloat() * (maxValue - minValue) + minValue;

    /// <summary> Get random <see cref="float"/> value in range of 0 and <paramref name="maxValue"/>. </summary>
    /// <param name="maxValue">Random value should be less or equal to this value.</param>
    public float NextFloat(float maxValue) => NextFloat() * maxValue;

    /// <summary> Get random <see cref="int"/> value. </summary>
    int Next();

    /// <summary> Get random <see cref="int"/> value in range of 0 and <paramref name="maxValue"/>. </summary>
    /// <param name="maxValue">Random value should be less or equal to this value.</param>
    int Next(int maxValue);

    /// <summary> Get random <see cref="int"/> value in range of <paramref name="minValue"/> and <paramref name="maxValue"/>. </summary>
    /// <param name="minValue">Random value should be greater or equal to this value.</param>
    /// <param name="maxValue">Random value should be less or equal to this value.</param>
    int Next(int minValue, int maxValue);

    /// <summary> Get random <see cref="byte"/> value. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte NextByte() => NextByte(byte.MaxValue);

    /// <summary> Get random <see cref="byte"/> value in range of 0 and <paramref name="maxValue"/>. </summary>
    /// <param name="maxValue">Random value should be less or equal to this value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte NextByte(byte maxValue) => NextByte(0, maxValue);

    /// <summary> Get random <see cref="byte"/> value in range of <paramref name="minValue"/> and <paramref name="maxValue"/>. </summary>
    /// <param name="minValue">Random value should be greater or equal to this value.</param>
    /// <param name="maxValue">Random value should be less or equal to this value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte NextByte(byte minValue, byte maxValue) => (byte)Next(minValue, maxValue);

    /// <summary> Get random <see cref="double"/> value. </summary>
    double NextDouble();

    /// <summary> Get random <see cref="double"/> value in range of 0 and <paramref name="maxValue"/>. </summary>
    /// <param name="maxValue">Random value should be less or equal to this value.</param>
    double Next(double maxValue) => NextDouble() * maxValue;

    /// <summary> Get random <see cref="double"/> value in range of <paramref name="minValue"/> and <paramref name="maxValue"/>. </summary>
    /// <param name="minValue">Random value should be greater or equal to this value.</param>
    /// <param name="maxValue">Random value should be less or equal to this value.</param>
    double NextDouble(double minValue, double maxValue) => NextDouble() * (maxValue - minValue) + minValue;

    /// <summary> Get random <see cref="TimeSpan"/> value in range of <see cref="TimeSpan.Zero"/> and <paramref name="maxTime"/>. </summary>
    /// <param name="maxTime">Random value should be less or equal to this value.</param>
    TimeSpan Next(TimeSpan maxTime);

    /// <summary> Get random <see cref="TimeSpan"/> value in range of <paramref name="minTime"/> and <paramref name="maxTime"/>. </summary>
    /// <param name="minTime">Random value should be greater or equal to this value.</param>
    /// <param name="maxTime">Random value should be less or equal to this value.</param>
    TimeSpan Next(TimeSpan minTime, TimeSpan maxTime);

    /// <summary> Fill buffer with random bytes. </summary>
    void NextBytes(byte[] buffer);

    /// <summary> Get random <see cref="Angle"/> value. </summary>
    public Angle NextAngle() => NextFloat() * MathF.Tau;

    /// <summary> Get random <see cref="Angle"/> value in range of 0 and <paramref name="maxValue"/>. </summary>
    /// <param name="maxValue">Random value should be less or equal to this value.</param>
    public Angle NextAngle(Angle maxValue) => NextFloat() * maxValue;

    /// <summary> Get random <see cref="Angle"/> value in range of <paramref name="minValue"/> and <paramref name="maxValue"/>. </summary>
    /// <param name="minValue">Random value should be greater or equal to this value.</param>
    /// <param name="maxValue">Random value should be less or equal to this value.</param>
    public Angle NextAngle(Angle minValue, Angle maxValue) => NextFloat() * (maxValue - minValue) + minValue;

    /// <summary>
    ///     Random vector, created from a uniform distribution of magnitudes and angles.
    /// </summary>
    /// <param name="maxMagnitude">Max value for randomized vector magnitude.</param>
    public Vector2 NextVector2(float maxMagnitude = 1) => NextVector2(0, maxMagnitude);

    /// <summary>
    ///     Random vector, created from a uniform distribution of magnitudes and angles.
    /// </summary>
    /// <param name="minMagnitude">Min value for randomized vector magnitude.</param>
    /// <param name="maxMagnitude">Max value for randomized vector magnitude.</param>
    /// <remarks>
    ///     In general, NextVector2(1) will tend to result in vectors with smaller magnitudes than
    ///     NextVector2Box(1,1), even if you ignored any vectors with a magnitude larger than one.
    /// </remarks>
    public Vector2 NextVector2(float minMagnitude, float maxMagnitude) => NextAngle().RotateVec(new Vector2(NextFloat(minMagnitude, maxMagnitude), 0));

    /// <summary>
    ///     Random vector, created from a uniform distribution of x and y coordinates lying inside some box.
    /// </summary>
    public Vector2 NextVector2Box(float minX, float minY, float maxX, float maxY) => new Vector2(NextFloat(minX, maxX), NextFloat(minY, maxY));

    /// <summary>
    ///     Random vector, created from a uniform distribution of x and y coordinates lying inside some box.
    ///     Box will have coordinates starting at [-<paramref name="maxAbsX"/> , -<paramref name="maxAbsY"/>]
    ///     and ending in [<paramref name="maxAbsX"/> , <paramref name="maxAbsY"/>]
    /// </summary>
    public Vector2 NextVector2Box(float maxAbsX = 1, float maxAbsY = 1) => NextVector2Box(-maxAbsX, -maxAbsY, maxAbsX, maxAbsY);

    /// <summary> Randomly switches positions in collection. </summary>
    void Shuffle<T>(IList<T> list);

    /// <summary> Randomly switches positions in collection. </summary>
    void Shuffle<T>(Span<T> list);

    /// <summary> Randomly switches positions in collection. </summary>
    void Shuffle<T>(ValueList<T> list);

    /// <summary>
    /// Get set amount of random items from collection.
    /// If <paramref name="allowDuplicates"/> is false and <paramref name="collection"/>
    /// is smaller then <paramref name="count"/> - returns shuffled <paramref name="collection"/> clone.
    /// If <paramref name="collection"/> is empty, and/or <paramref name="count"/> is 0, reutns empty.
    /// </summary>
    /// <param name="collection">Collection from which items should be picked.</param>
    /// <param name="count">Count of random items to be picked.</param>
    /// <param name="allowDuplicates">Marker, are duplicate items allowed in results.</param>
    IReadOnlyCollection<T> GetItems<T>(IList<T> collection, int count, bool allowDuplicates = true);

    /// <summary>
    /// Get set amount of random items from collection.
    /// If <paramref name="allowDuplicates"/> is false and <paramref name="collection"/>
    /// is smaller then <paramref name="count"/> - returns shuffled <paramref name="collection"/> clone.
    /// If <paramref name="collection"/> is empty, and/or <paramref name="count"/> is 0, reutns empty.
    /// </summary>
    /// <param name="collection">Collection from which items should be picked.</param>
    /// <param name="count">Count of random items to be picked.</param>
    /// <param name="allowDuplicates">Marker, are duplicate items allowed in results.</param>
    ValueList<T> GetItems<T>(ValueList<T> collection, int count, bool allowDuplicates = true);

    /// <summary>
    /// Get set amount of random items from collection.
    /// If <paramref name="allowDuplicates"/> is false and <paramref name="collection"/>
    /// is smaller then <paramref name="count"/> - returns shuffled <paramref name="collection"/> clone.
    /// If <paramref name="collection"/> is empty, and/or <paramref name="count"/> is 0, reutns empty.
    /// </summary>
    /// <param name="collection">Collection from which items should be picked.</param>
    /// <param name="count">Count of random items to be picked.</param>
    /// <param name="allowDuplicates">Marker, are duplicate items allowed in results.</param>
    Span<T> GetItems<T>(Span<T> collection, int count, bool allowDuplicates = true);
}

public static class RandomHelpers
{
    public static void Shuffle<T>(this System.Random random, IList<T> list)
    {
        var n = list.Count;
        while (n > 1)
        {
            n -= 1;
            var k = random.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }

    public static bool Prob(this System.Random random, double chance)
    {
        return random.NextDouble() < chance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte NextByte(this System.Random random, byte maxValue)
    {
        return NextByte(random, 0, maxValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte NextByte(this System.Random random)
    {
        return NextByte(random, byte.MaxValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte NextByte(this System.Random random, byte minValue, byte maxValue)
    {
        return (byte)random.Next(minValue, maxValue);
    }
}
