using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;
using Random = UnityEngine.Random;

public class Deck : MonoBehaviour
{
    //Attributes
    public List<Card> deck;
    public GameObject cardPrefab;
    public GameManager gameManager;
    
    
    // Awake runs before any Start() in the scene, guaranteeing the deck is
    // fully built before GameManager.Start() tries to deal from it. Using
    // Start() here was a latent bug - it happened to look fine only because
    // undealt cards used to spawn visible regardless of whether dealing worked.
    void Awake()
    {
        BuildDeck();
        Shuffle();
        Debug.Log(deck.Count);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // Clicking the deck draws a card. No drag involved here since there's
    // nothing individual to drag - it's a single face-down stack.
    void OnMouseDown()
    {
        gameManager.DrawFromDeck();
    }

    private void BuildDeck()
    {
        // Tranca uses two 52-card decks plus jokers (108 cards total: 2x52 + 4 jokers)
        for (int d = 0; d < 2; d++)
        {
            foreach (Card.Suit suit in System.Enum.GetValues(typeof(Card.Suit)))
            {
                if (suit == Card.Suit.None) continue; // reserved for jokers, not a real suit

                foreach (Card.Rank rank in System.Enum.GetValues(typeof(Card.Rank)))
                {
                    if (rank == Card.Rank.Joker) continue; // added separately below

                    GameObject cardObject = Instantiate(cardPrefab, transform.position, Quaternion.identity, transform);
                    Card card = cardObject.GetComponent<Card>();
                    card.SetCard(suit, rank);
                    card.gameObject.SetActive(false); // hidden until dealt - stays face-down/invisible in the deck
                    deck.Add(card);
                }
            }

            // 2 jokers per deck = 4 total
            for (int j = 0; j < 2; j++)
            {
                GameObject cardObject = Instantiate(cardPrefab, transform.position, Quaternion.identity, transform);
                Card card = cardObject.GetComponent<Card>();
                card.SetCard(Card.Suit.None, Card.Rank.Joker);
                card.gameObject.SetActive(false);
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

    // Refills the draw deck from a released morto pile (used when the deck runs dry)
    // and reshuffles so the refill isn't in a known order.
    public void RefillFrom(List<Card> cards)
    {
        foreach (var c in cards)
            c.gameObject.SetActive(false);
        deck.AddRange(cards);
        Shuffle();
    }

    // Deals an 11-card morto (reserve pile) and hides the cards until picked up.
    public List<Card> DealMorto()
    {
        List<Card> morto = new List<Card>();
        for (int i = 0; i < 11; i++)
        {
            Card c = Deal();
            if (c == null) break;
            c.gameObject.SetActive(false);
            morto.Add(c);
        }
        return morto;
    }
}