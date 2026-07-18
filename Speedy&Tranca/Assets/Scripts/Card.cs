using UnityEngine;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;

public class Card : NetworkBehaviour
{
    // Attributes for the card class
    public TMP_Text cardLabel;
    public enum Suit { Spades, Clubs, Hearts, Diamonds, None } // None used for Jokers
    public enum Rank { Joker = 0, Two = 2, Three = 3, Four = 4, Five = 5, Six = 6, Seven = 7, Eight = 8, Nine = 9, Ten = 10, Jack = 11, Queen = 12, King = 13, Ace = 14 }

    // Local mirrors of the two NetworkVariables below, kept up to date via
    // OnValueChanged so every OTHER script in the project (TrancaScoring,
    // Meld, DisplayHand, etc.) can keep reading card.suit / card.rank exactly
    // like before - nothing outside this file needs to change for that part.
    public Suit suit;
    public Rank rank;

    // Only the server ever assigns what a card actually is (see SetCard).
    // Everyone is allowed to read it - see the "hiding hands" note in chat
    // if you want opponents' cards to actually be secret over the network.
    private NetworkVariable<Suit> netSuit = new NetworkVariable<Suit>(
        Suit.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Rank> netRank = new NetworkVariable<Rank>(
        Rank.Joker, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Replaces raw gameObject.SetActive(...) calls. SetActive is a purely
    // local Unity call - it never used to affect the other machine's copy of
    // this object at all. This makes "face up / in play" vs "hidden" an
    // actual networked, synced piece of state.
    private NetworkVariable<bool> netVisible = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [HideInInspector] public Player owner;

    private bool isDragging;
    private Vector3 dragOffset;
    private float dragZ;
    public bool IsDragging => isDragging;

    // --- Tranca rule helpers ---
    public bool IsWild => rank == Rank.Two || rank == Rank.Joker;
    public bool IsBlackThree => rank == Rank.Three && (suit == Suit.Spades || suit == Suit.Clubs);
    public bool IsRedThree => rank == Rank.Three && (suit == Suit.Hearts || suit == Suit.Diamonds);

    public override void OnNetworkSpawn()
    {
        netSuit.OnValueChanged += (_, newVal) => { suit = newVal; UpdateDisplay(); };
        netRank.OnValueChanged += (_, newVal) => { rank = newVal; UpdateDisplay(); };
        netVisible.OnValueChanged += (_, newVal) => gameObject.SetActive(newVal);

        // Pick up whatever the server already set before this client spawned in.
        suit = netSuit.Value;
        rank = netRank.Value;
        gameObject.SetActive(netVisible.Value);
        UpdateDisplay();
    }

    // Only the server calls this (Deck.BuildDeck runs server-only now).
    public void SetCard(Suit newSuit, Rank newRank)
    {
        if (!IsServer)
        {
            Debug.LogWarning("SetCard called on a non-server instance - ignored.");
            return;
        }
        netSuit.Value = newSuit;
        netRank.Value = newRank;
    }

    // Replaces gameObject.SetActive(bool) everywhere else in the project.
    public void SetVisible(bool visible)
    {
        if (!IsServer)
        {
            Debug.LogWarning("SetVisible called on a non-server instance - ignored.");
            return;
        }
        netVisible.Value = visible;
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
        // --- Ownership gate ---
        // A card that belongs to a player's hand/table state can only be
        // picked up by the client that actually controls that player. This
        // is what stops Player Two's client from ever being able to drag
        // one of Player One's cards (and vice versa). Cards with no owner
        // (sitting on the shared discard pile) are handled separately below.
        if (owner != null && !owner.IsOwner) return;

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
            owner.OnCardDragEnd(this, zone);
        }
        else if (zone != null && zone.zoneType == DropZone.ZoneType.Hand)
        {
            // Taking the discard pile. Only allow this through a hand zone
            // the local client actually controls - otherwise Player Two
            // could drag a pile card into Player One's zone.
            if (zone.owner != null && zone.owner.IsOwner)
                zone.owner.RequestTakeDiscardPile();
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