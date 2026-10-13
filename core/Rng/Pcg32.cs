using System.Text;

namespace Game.Core;

/// <summary>Источник случайности ядра. Только целые числа (раздел 10).</summary>
public interface IRandom
{
    uint NextUInt();

    /// <summary>Равномерное целое в [0, n), n &gt; 0.</summary>
    int Int(int n);
}

/// <summary>
/// PCG32 (XSH RR, 64-битное состояние). Состояние — два поля, сериализуется вместе с матчем.
/// </summary>
public sealed class Pcg32 : IRandom
{
    // Константы алгоритма PCG, а не правил игры.
    private const ulong Multiplier = 6364136223846793005UL;

    public ulong State { get; set; }
    public ulong Inc { get; set; }

    /// <summary>Для десериализации.</summary>
    public Pcg32() { Inc = 1; }

    public Pcg32(ulong initState, ulong initSeq)
    {
        State = 0;
        Inc = (initSeq << 1) | 1UL;
        NextUInt();
        State = unchecked(State + initState);
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong old = State;
        State = unchecked(old * Multiplier + Inc);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
    }

    public int Int(int n)
    {
        if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n), "n должно быть > 0");
        uint bound = (uint)n;
        // Отбрасываем «хвост», чтобы распределение было ровным.
        uint threshold = unchecked(0u - bound) % bound;
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold) return (int)(r % bound);
        }
    }

    public Pcg32 Clone() => new() { State = State, Inc = Inc };
}

/// <summary>Именованные потоки RNG (раздел 10): planning[side], combat[round].</summary>
public static class RngStreams
{
    public const string Planning = "planning";
    public const string Combat = "combat";

    public static Pcg32 PlanningStream(ulong matchSeed, Side side) => Derive(matchSeed, Planning, (int)side);

    public static Pcg32 CombatStream(ulong matchSeed, int round) => Derive(matchSeed, Combat, round);

    /// <summary>derive(matchSeed, label, index): детерминированный независимый поток.</summary>
    public static Pcg32 Derive(ulong matchSeed, string label, int index)
    {
        ulong h = SplitMix(matchSeed);
        h = SplitMix(h ^ Fnv1a(label));
        h = SplitMix(h ^ (ulong)(uint)index);
        ulong seq = SplitMix(h);
        return new Pcg32(h, seq);
    }

    private static ulong SplitMix(ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    private static ulong Fnv1a(string s)
    {
        unchecked
        {
            ulong h = 0xCBF29CE484222325UL;
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                h ^= b;
                h *= 0x100000001B3UL;
            }
            return h;
        }
    }
}

public static class RandomExtensions
{
    /// <summary>Проценты — только так: rng.int(100) &lt; p (раздел 10).</summary>
    public static bool Chance(this IRandom rng, int percent) => rng.Int(100) < percent;

    public static T Pick<T>(this IRandom rng, IReadOnlyList<T> items)
    {
        if (items.Count == 0) throw new ArgumentException("Пустой список для выбора", nameof(items));
        return items[rng.Int(items.Count)];
    }

    /// <summary>Тасовка Фишера — Йетса на месте.</summary>
    public static void Shuffle<T>(this IRandom rng, IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.Int(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>N случайных без повторов (9.3); если элементов меньше N — все. Порядок — порядок выбора.</summary>
    public static List<T> Sample<T>(this IRandom rng, IReadOnlyList<T> items, int count)
    {
        var pool = new List<T>(items);
        var result = new List<T>();
        while (result.Count < count && pool.Count > 0)
        {
            int j = rng.Int(pool.Count);
            result.Add(pool[j]);
            pool.RemoveAt(j);
        }
        return result;
    }
}
