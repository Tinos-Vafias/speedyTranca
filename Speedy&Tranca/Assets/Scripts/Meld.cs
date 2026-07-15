using System.Collections.Generic;
using System.Linq;

public class Meld
{
    public enum MeldType { Set, Run }

    public MeldType type;
    public List<Card> cards = new List<Card>();

    public bool IsCanasta => cards.Count >= 7;                      // a "tranca" once it hits 7
    public bool IsClean => cards.TrueForAll(c => !c.IsWild);        // clean vs dirty tranca

    // --- Static validators, used before creating/extending a Meld ---

    public static bool IsValidSet(List<Card> cards)
    {
        if (cards.Count < 3) return false;
        var nonWild = cards.Where(c => !c.IsWild).ToList();
        if (nonWild.Count == 0) return false; // can't have an all-wild "set" of unspecified rank
        return nonWild.Select(c => c.rank).Distinct().Count() <= 1;
    }

    public static bool IsValidRun(List<Card> cards)
    {
        if (cards.Count < 3) return false;

        var nonWild = cards.Where(c => !c.IsWild).ToList();
        if (nonWild.Count == 0) return false; // need at least one anchor card to fix suit/position

        // All non-wild cards must share a suit
        if (nonWild.Select(c => c.suit).Distinct().Count() > 1) return false;

        // Threes never reach this function in practice: black threes can't be melded
        // at all, and red threes are laid down separately the instant they're drawn.
        // Guarded here too in case validation is ever called on unfiltered input.
        if (nonWild.Any(c => c.rank == Card.Rank.Three)) return false;

        // Ace sits high, after King
        int RankValue(Card.Rank r) => r == Card.Rank.Ace ? 14 : (int)r;

        var sortedValues = nonWild.Select(c => RankValue(c.rank)).OrderBy(v => v).ToList();

        // No duplicate ranks allowed within a single run
        if (sortedValues.Distinct().Count() != sortedValues.Count) return false;

        int wildsAvailable = cards.Count - nonWild.Count;

        // Sum the internal gaps between consecutive non-wild ranks —
        // each missing rank in between needs one wild to stand in for it.
        int gapsNeeded = 0;
        for (int i = 1; i < sortedValues.Count; i++)
            gapsNeeded += sortedValues[i] - sortedValues[i - 1] - 1;

        if (gapsNeeded > wildsAvailable) return false;

        // Any wilds left over after filling internal gaps extend the run
        // outward at either end — valid as long as there's rank room to do so.
        // Playable range is Four (lowest, since Threes are excluded) through Ace (14).
        int leftoverWilds = wildsAvailable - gapsNeeded;
        int roomBelow = sortedValues.First() - 4;
        int roomAbove = 14 - sortedValues.Last();

        if (leftoverWilds > roomBelow + roomAbove) return false;

        return true;
    }
}