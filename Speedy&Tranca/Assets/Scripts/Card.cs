using UnityEngine;
using System.Collections.Generic;
using TMPro; 
public class Card : MonoBehaviour
{
    // Attributes for the card class
    public TMP_Text cardLabel;
    public enum Suit { Spades, Clubs, Hearts, Diamonds, None } // None used for Jokers
    public enum Rank { Joker = 0, Two = 2, Three = 3, Four = 4, Five = 5, Six = 6, Seven = 7, Eight = 8, Nine = 9, Ten = 10, Jack = 11, Queen = 12, King = 13, Ace = 14 }
    
    public Suit suit;
    public Rank rank;

    [HideInInspector] public Player owner;

    private bool isDragging;
    private Vector3 dragOffset;
    private float dragZ;
    public bool IsDragging => isDragging;

    // --- Tranca rule helpers ---
    public bool IsWild => rank == Rank.Two || rank == Rank.Joker;
    public bool IsBlackThree => rank == Rank.Three && (suit == Suit.Spades || suit == Suit.Clubs);
    public bool IsRedThree => rank == Rank.Three && (suit == Suit.Hearts || suit == Suit.Diamonds);

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SetCard(Suit newSuit, Rank newRank)
    {
        suit = newSuit;
        rank = newRank;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (cardLabel != null)
        {
            cardLabel.text = (rank == Rank.Joker) ? "JOKER" : $"{rank}\n{suit}";
            cardLabel.color = Color.black; 
        }
    }
    
    void OnMouseDown()
    {
        isDragging = true;
        dragZ = Camera.main.WorldToScreenPoint(transform.position).z;
        dragOffset = transform.position - GetMouseWorldPos();

        GetComponent<SpriteRenderer>().sortingOrder = 100; // bring to front while held
        owner?.OnCardDragStart(this);
    }

    void OnMouseDrag()
    {
        if (!isDragging) return;
        transform.position = GetMouseWorldPos() + dragOffset;
    }

    void OnMouseUp()
    {
        if (!isDragging) return;
        isDragging = false;

        DropZone zone = FindDropZoneAtCurrentPosition();

        if (owner != null)
        {
            // Normal case: card belongs to a player's hand (or was already
            // theirs on the table) - let their own drag-end logic decide what happens.
            owner.OnCardDragEnd(this, zone);
        }
        else if (zone != null && zone.zoneType == DropZone.ZoneType.Hand)
        {
            // This card has no owner, meaning it currently lives on the discard
            // pile (DiscardPile.Discard clears owner on pickup). Dropping it into
            // a hand zone means "take the whole pile," not "move this one card."
            zone.owner?.RequestTakeDiscardPile();
        }
    }

    private DropZone FindDropZoneAtCurrentPosition()
    {
        Collider2D[] hits = Physics2D.OverlapPointAll(transform.position);
        foreach (var hit in hits)
        {
            DropZone zone = hit.GetComponent<DropZone>();
            if (zone != null) return zone;
        }
        return null;
    }

    private Vector3 GetMouseWorldPos()
    {
        Vector3 mousePoint = Input.mousePosition;
        mousePoint.z = dragZ;
        return Camera.main.ScreenToWorldPoint(mousePoint);
    }
}