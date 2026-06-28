using UnityEngine;
using System.Collections.Generic;
public class Card : MonoBehaviour
{
    // Attributes for the card class
    public enum Suit { Spades, Clubs, Hearts, Diamonds}
    public enum Rank { Two = 2, Three = 3, Four = 4, Five = 5, Six = 6, Seven = 7, Eight = 8, Nine = 9, Ten = 10, Jack = 11, Queen = 12, King = 13, Ace = 14 }
    
    public Suit suit;
    public Rank rank;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        SetCard(Suit.Spades, Rank.Two);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SetCard(Suit newSuit, Rank newRank)
    {
        suit = newSuit;
        rank = newRank;
    }
}
