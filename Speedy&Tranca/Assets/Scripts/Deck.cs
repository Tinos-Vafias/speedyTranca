using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using Random = UnityEngine.Random;

public class Deck : NetworkBehaviour
{
    //Attributes
    public List<Card> deck;
    public GameObject cardPrefab; // must have a NetworkObject component, and be
                                   // registered in NetworkManager's Network Prefabs list
    public GameManager gameManager;

    public override void OnNetworkSpawn()
    {
        // Only the server builds and shuffles the deck. If every machine did
        // this independently, each would produce a different random order
        // and the two clients' views of "what card is where" would diverge
        // immediately - this used to run in Awake() on every copy of the game.
        if (!IsServer) return;

        BuildDeck();
        Shuffle();
        Debug.Log(deck.Count);
    }

    // Clicking the deck asks the server for a card - it's no longer decided
    // locally. Routed through whichever Player object this client owns, so
    // GameManager can check it's actually that player's turn before dealing.
    void OnMouseDown()
    {
        Player local = gameManager.LocalPlayer;
        if (local == null)
        {
            Debug.LogWarning("No local player assigned yet - can't draw.");
            return;
        }
        local.RequestDrawFromDeckServerRpc();
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
                    SpawnCard(suit, rank);
                }
            }

            // 2 jokers per deck = 4 total
            for (int j = 0; j < 2; j++)
                SpawnCard(Card.Suit.None, Card.Rank.Joker);
        }
    }

    private void SpawnCard(Card.Suit suit, Card.Rank rank)
    {
        GameObject cardObject = Instantiate(cardPrefab, transform.position, Quaternion.identity, transform);
        Card card = cardObject.GetComponent<Card>();

        // Spawn onto the network BEFORE writing to the card's NetworkVariables -
        // a NetworkObject must be spawned before its NetworkVariables can be written.
        cardObject.GetComponent<NetworkObject>().Spawn();

        card.SetCard(suit, rank);
        card.SetVisible(false); // hidden until dealt
        deck.Add(card);
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
        if (!IsServer) return null; // deck order is server-only knowledge

        if (deck.Count > 0)
        {
            Card selectedCard = deck[deck.Count - 1];
            deck.RemoveAt(deck.Count - 1);
            return selectedCard;
        }
        Debug.Log("Empty deck!");
        return null;
    }

    // Refills the draw deck from a released morto pile (used when the deck runs dry)
    // and reshuffles so the refill isn't in a known order.
    public void RefillFrom(List<Card> cards)
    {
        if (!IsServer) return;

        foreach (var c in cards)
            c.SetVisible(false);
        deck.AddRange(cards);
        Shuffle();
    }

    // Deals an 11-card morto (reserve pile) and hides the cards until picked up.
    public List<Card> DealMorto()
    {
        if (!IsServer) return new List<Card>();

        List<Card> morto = new List<Card>();
        for (int i = 0; i < 11; i++)
        {
            Card c = Deal();
            if (c == null) break;
            c.SetVisible(false);
            morto.Add(c);
        }
        return morto;
    }
}