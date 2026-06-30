using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;
using Random = UnityEngine.Random;

public class Deck : MonoBehaviour
{
    //Attributes
    public List<Card> deck;
    public GameObject cardPrefab;
    
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        BuildDeck();
        Shuffle();
        Debug.Log(deck.Count);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void BuildDeck()
    {
        foreach (Card.Suit suit in System.Enum.GetValues(typeof(Card.Suit)))
        {
            foreach (Card.Rank rank in System.Enum.GetValues(typeof(Card.Rank)))
            {
                GameObject cardObject = Instantiate(cardPrefab);
                Card card = cardObject.GetComponent<Card>();
                card.SetCard(suit, rank);
                deck.Add(card);
            } 
        }
    }

    private void Shuffle()
    {
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    public Card Deal()
    {
        if (deck.Count > 0)
        {
            Card selectedCard = deck[deck.Count-1];
            deck.RemoveAt(deck.Count-1);
            return selectedCard;
        }
        Debug.Log("Empty deck!");
        return null;
    }
}
