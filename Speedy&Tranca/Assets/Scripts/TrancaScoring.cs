using UnityEngine;

public static class TrancaScoring
{
    public static int PointValue(Card card)
    {
        if (card.rank == Card.Rank.Joker) return 50;
        if (card.IsWild) return 20;
        if (card.rank == Card.Rank.Ace) return 20;
        if ((int)card.rank >= 8) return 10;
        return 5;
    }

    public static int ScorePlayer(Player p)
    {
        int total = 0;

        foreach (var meld in p.TableMelds)
        {
            if (meld.IsCanasta)
            {
                total += meld.IsClean ? 200 : 100;
            }

            foreach (var c in meld.cards)
            {
                total += PointValue(c);
            }
        }

        int redThrees = p.PlayedRedThrees.Count;
        bool hasAnyCanasta = p.TableMelds.Exists(m => m.IsCanasta);
        total += hasAnyCanasta ? redThrees * 100 : redThrees * -100;
 
        foreach (var c in p.Hand)
            total -= PointValue(c); // cards left in hand count against you
 
        return total;
    }
}
