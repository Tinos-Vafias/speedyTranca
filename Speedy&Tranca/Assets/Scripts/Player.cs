using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UIElements;
using UnityEngine.XR;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{
    public Deck deck;
    // Attributes regarding the player
    // For game conditions
    private int score = 0;
    private bool hasWon = false;
    
    // For cards
    public GameObject cardPrefab;
    private List<Card> hand = new List<Card>();
    private int numCards;
    public Transform handContainer;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        numCards = hand.Count;
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            generateHand();
            for (int i = 0; i < numCards; i++)
            {
                Debug.Log($"Card: {hand[i].rank} of {hand[i].suit}");
            }
        }
    }

    private void generateHand()
    {
        for (int i = 0; i < 11; i++)
        {
            Card dealt = deck.Deal();
            dealt.owner = this;          // NEW: so the card can notify us
            hand.Add(dealt);
        }
        numCards = hand.Count;
        DisplayHand();
    }

    void DisplayHand()
    {
        foreach (Card card in hand)
            card.gameObject.SetActive(false);

        int count = hand.Count;
        float fanAngle = 30f;
        float radius = 15f;
        // Push the arc center further down so cards fan upward from bottom center
        Vector3 arcCenter = handContainer.position + new Vector3(0, -radius + 2f, 0);

        for (int i = 0; i < count; i++)
        {
            Card card = hand[i];
            card.gameObject.SetActive(true);

            if (card.IsDragging) continue;
            
            float t = count > 1 ? i / (float)(count - 1) : 0.5f;
            float angle = Mathf.Lerp(fanAngle / 2, -fanAngle / 2, t);

            float radian = angle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(radian), Mathf.Cos(radian), 0) * radius;
            card.transform.position = arcCenter + offset;

            card.transform.rotation = Quaternion.Euler(0, 0, angle);
            card.GetComponent<SpriteRenderer>().sortingOrder = i;
        }
    }

    public void RemoveCard(Card card)
    {
        card.gameObject.SetActive(false);
        hand.Remove(card);
        DisplayHand();
    }

    public void AddCard(Card card)
    {
        hand.Add(card);
        DisplayHand();
        card.owner = this;
    }
    
    // NEW: drag hooks called by Card
    public void OnCardDragStart(Card card)
    {
        // Hook for later: highlight a play zone, disable other input, etc.
    }

    public void OnCardDragEnd(Card card)
    {
        // For now: snap back into hand formation.
        // Later, check a drop zone here before calling DisplayHand()
        // to support actually "playing" the card instead of returning it.
        DisplayHand();
    }
}
