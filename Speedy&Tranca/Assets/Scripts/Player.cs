using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using UnityEngine.XR;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{
    public Deck deck;
    public GameManager gameManager;
    // Attributes regarding the player
    // For game conditions
    private int score = 0;
    private bool hasWon = false;
    
    // For cards
    public GameObject cardPrefab;
    private List<Card> hand = new List<Card>();
    private int numCards;
    public Transform handContainer;
    
    public bool IsMyTurn { get; private set; }

    // --- Melds / table state ---
    public Transform meldContainer;
    public Transform redThreeContainer;
    public Transform stagingZoneAnchor; // single staging area for building a brand-new meld
    private List<Meld> tableMelds = new List<Meld>();
    private List<Card> playedRedThrees = new List<Card>();
    private List<Card> stagingMeld = new List<Card>();
    private List<GameObject> meldZoneObjects = new List<GameObject>();

    // --- Morto (reserve pile), one use per player ---
    private List<Card> morto = new List<Card>();
    private bool hasUsedMorto;

    // Public read-only accessors, e.g. for TrancaScoring
    public List<Meld> TableMelds => tableMelds;
    public List<Card> PlayedRedThrees => playedRedThrees;
    public List<Card> Hand => hand;

    // --- Meld layout tuning ---
    public float cardSpacingWithinMeld = 0.45f;  // overlap between cards in the same meld
    public float gapBetweenMelds = 1.3f;         // breathing room between separate melds
    public float rowWidthLimit = 12f;            // wrap to a new row past this width
    public float rowSpacingY = 1.0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        numCards = hand.Count;
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void generateHand()
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
        // Hook for later: highlight valid drop zones, disable other input, etc.
    }

    public void OnCardDragEnd(Card card, DropZone zone)
    {
        // A card already sitting in the staging area is a distinct case: any
        // drop except back onto its own staging zone cancels it out of staging
        // and returns it to hand, rather than being treated as a fresh hand play.
        if (stagingMeld.Contains(card))
        {
            HandleStagingCardDragEnd(card, zone);
            return;
        }

        if (zone == null || zone.zoneType == DropZone.ZoneType.Hand)
        {
            DisplayHand(); // no valid drop, or dropped back into own hand - snap back
            return;
        }

        // Reject drops onto another player's zones outright.
        if (zone.owner != null && zone.owner != this)
        {
            DisplayHand();
            return;
        }

        // Melding, discarding, and extending only make sense after this player
        // has actually drawn this turn. Reject immediately with a clear reason
        // rather than letting cards drift into staging and get stuck there
        // until commit time, which is what was happening before this check.
        if (gameManager.currentPhase != GameManager.TurnPhase.Meld)
        {
            Debug.Log($"Can't do that yet - current phase is {gameManager.currentPhase}. Draw a card first.");
            DisplayHand();
            return;
        }

        switch (zone.zoneType)
        {
            case DropZone.ZoneType.DiscardPile:
                TryDiscard(card);
                break;
            case DropZone.ZoneType.NewMeldStaging:
                TryAddToStaging(card);
                break;
            case DropZone.ZoneType.ExistingMeld:
                TryExtendMeld(zone.linkedMeld, card);
                break;
        }
    }

    private void HandleStagingCardDragEnd(Card card, DropZone zone)
    {
        if (zone != null && zone.zoneType == DropZone.ZoneType.NewMeldStaging && zone.owner == this)
        {
            // Dropped back onto its own staging zone - no change, just re-lay-out.
            DisplayStaging();
            return;
        }

        // Any other drop target (including nowhere, or back into hand) cancels
        // it out of staging and returns it to hand. This is also how a group
        // stuck in staging after a rejected commit (see CommitStaging) gets
        // unstuck - drag each card back out individually.
        stagingMeld.Remove(card);
        hand.Add(card);
        DisplayHand();
        DisplayStaging();
    }

    private void TryExtendMeld(Meld meld, Card card)
    {
        bool success = gameManager.TryExtendMeld(this, meld, card);
        if (!success)
        {
            DisplayHand(); // invalid extension, or illegal go-out - snap back
        }
        // On success, GameManager already called Player.ExtendMeld, which
        // handles moving the card and refreshing the display.
    }

    private void TryDiscard(Card card)
    {
        bool success = gameManager.DiscardCard(card);
        if (!success)
        {
            // Illegal discard (e.g. would end the hand with no natural canasta) - snap back.
            DisplayHand();
        }
        // On success the card has already left the hand and moved to the
        // discard pile via GameManager/DiscardPile, so nothing more to do here.
    }

    private void TryAddToStaging(Card card)
    {
        List<Card> proposed = new List<Card>(stagingMeld) { card };

        bool compatibleAsSet = PartialSetCompatible(proposed);
        bool compatibleAsRun = PartialRunCompatible(proposed);

        if (!compatibleAsSet && !compatibleAsRun)
        {
            Debug.Log($"Staging: {card.rank} of {card.suit} rejected - doesn't fit as a set or run with the current staged group.");
            DisplayHand(); // doesn't fit either shape at all - reject, snap back
            return;
        }

        hand.Remove(card);
        stagingMeld.Add(card);
        DisplayHand();
        DisplayStaging();

        Debug.Log($"Staging now has {stagingMeld.Count} card(s): " +
            string.Join(", ", stagingMeld.Select(c => $"{c.rank} of {c.suit}")));

        if (stagingMeld.Count >= 3)
        {
            if (Meld.IsValidSet(stagingMeld))
            {
                Debug.Log("Staged group is a valid SET - attempting to commit.");
                CommitStaging(Meld.MeldType.Set);
            }
            else if (Meld.IsValidRun(stagingMeld))
            {
                Debug.Log("Staged group is a valid RUN - attempting to commit.");
                CommitStaging(Meld.MeldType.Run);
            }
            else
            {
                Debug.Log("Staged group has 3+ cards but isn't a valid set OR run yet - staying staged.");
            }
        }
    }

    private void CommitStaging(Meld.MeldType type)
    {
        List<Card> toCommit = new List<Card>(stagingMeld);
        bool success = gameManager.TryPlayMeld(toCommit, type);
        if (success)
        {
            Debug.Log("Meld committed successfully.");
            stagingMeld.Clear();
        }
        else
        {
            Debug.Log("Meld commit REJECTED by GameManager.TryPlayMeld - check the log line just above " +
                "this one for the specific reason. Cards remain staged; drag them back to hand individually to cancel.");
        }
    }

    private bool PartialSetCompatible(List<Card> cards)
    {
        var nonWild = cards.Where(c => !c.IsWild).ToList();
        return nonWild.Select(c => c.rank).Distinct().Count() <= 1;
    }

    private bool PartialRunCompatible(List<Card> cards)
    {
        var nonWild = cards.Where(c => !c.IsWild).ToList();
        if (nonWild.Count == 0) return true;
        // Only rejects a clear suit mismatch early. Whether available wilds can
        // actually bridge the eventual rank gaps is only verified once the
        // staged group hits 3+ cards, via Meld.IsValidRun.
        return nonWild.Select(c => c.suit).Distinct().Count() <= 1;
    }

    private void DisplayStaging()
    {
        for (int i = 0; i < stagingMeld.Count; i++)
        {
            stagingMeld[i].gameObject.SetActive(true);
            stagingMeld[i].transform.position = stagingZoneAnchor.position
                + new Vector3(i * cardSpacingWithinMeld, 0, 0);
            stagingMeld[i].GetComponent<SpriteRenderer>().sortingOrder = i;
        }
    }

    public void ExtendMeld(Meld meld, Card card)
    {
        hand.Remove(card);
        meld.cards.Add(card);
        DisplayHand();
        DisplayMelds();
    }

    public void RequestTakeDiscardPile()
    {
        gameManager.DrawFromDiscardPile();
    }

    public void BeginTurn()
    {
        IsMyTurn = true;
    }

    public void EndTurn()
    {
        IsMyTurn = false;
    }

    public void DealStartingHand()
    {
        generateHand();
    }

    // --- Melds ---

    public void PlayMeld(List<Card> cards, Meld.MeldType type)
    {
        foreach (var c in cards) hand.Remove(c);
        tableMelds.Add(new Meld { type = type, cards = cards });
        DisplayHand();
        DisplayMelds();
    }

    void DisplayMelds()
    {
        // Rebuild every meld's drop-zone collider from scratch each call, since
        // layout (and therefore where each meld's zone should sit) changes
        // whenever a meld is added to or extended.
        foreach (var go in meldZoneObjects) Destroy(go);
        meldZoneObjects.Clear();

        // Pack melds into rows so a wide set of trancas wraps instead of running off the table
        List<float> meldWidths = tableMelds.Select(m =>
            (m.cards.Count - 1) * cardSpacingWithinMeld).ToList();

        List<List<Meld>> rows = new List<List<Meld>>();
        List<Meld> currentRow = new List<Meld>();
        float currentRowWidth = 0f;

        for (int i = 0; i < tableMelds.Count; i++)
        {
            float w = meldWidths[i];
            float projected = currentRowWidth + w + (currentRow.Count > 0 ? gapBetweenMelds : 0);

            if (projected > rowWidthLimit && currentRow.Count > 0)
            {
                rows.Add(currentRow);
                currentRow = new List<Meld>();
                currentRowWidth = 0f;
            }

            currentRow.Add(tableMelds[i]);
            currentRowWidth += w + (currentRow.Count > 1 ? gapBetweenMelds : 0);
        }
        if (currentRow.Count > 0) rows.Add(currentRow);

        float startY = (rows.Count - 1) * rowSpacingY / 2f;

        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            float totalWidth = row.Sum(m => (m.cards.Count - 1) * cardSpacingWithinMeld)
                              + gapBetweenMelds * (row.Count - 1);
            float cursorX = -totalWidth / 2f;
            float y = startY - r * rowSpacingY;

            foreach (var meld in row)
            {
                float meldWidth = (meld.cards.Count - 1) * cardSpacingWithinMeld;

                for (int i = 0; i < meld.cards.Count; i++)
                {
                    meld.cards[i].gameObject.SetActive(true);
                    meld.cards[i].transform.position = meldContainer.position
                        + new Vector3(cursorX + i * cardSpacingWithinMeld, y, 0);
                    meld.cards[i].GetComponent<SpriteRenderer>().sortingOrder = i;
                }

                // Drop-zone collider covering this meld, so a dragged card
                // released here extends this specific meld.
                GameObject zoneObj = new GameObject($"MeldZone_{tableMelds.IndexOf(meld)}");
                zoneObj.transform.SetParent(meldContainer);
                zoneObj.transform.position = meldContainer.position
                    + new Vector3(cursorX + meldWidth / 2f, y, 0);
                BoxCollider2D col = zoneObj.AddComponent<BoxCollider2D>();
                col.size = new Vector2(meldWidth + cardSpacingWithinMeld + 0.4f, 1.0f);
                DropZone zone = zoneObj.AddComponent<DropZone>();
                zone.zoneType = DropZone.ZoneType.ExistingMeld;
                zone.owner = this;
                zone.linkedMeld = meld;
                meldZoneObjects.Add(zoneObj);

                cursorX += meldWidth + gapBetweenMelds;
            }
        }
    }

    // --- Red threes: drawn, then immediately laid down face up, never held ---

    public void PlayRedThree(Card card)
    {
        hand.Remove(card);
        playedRedThrees.Add(card);
        card.gameObject.SetActive(true);
        card.transform.position = redThreeContainer.position + new Vector3(playedRedThrees.Count * 0.6f, 0, 0);
    }

    // --- Morto pickup ---

    public void SetMorto(List<Card> mortoCards)
    {
        morto = mortoCards;
    }

    // Query without consuming - used to decide whether "going out" is currently legal.
    public bool HasMortoAvailable => !hasUsedMorto && morto.Count > 0;

    public bool TryPickUpMorto()
    {
        if (hasUsedMorto || morto.Count == 0) return false;
        foreach (var c in morto)
        {
            c.owner = this;
            hand.Add(c);
        }
        morto.Clear();
        hasUsedMorto = true;
        DisplayHand();
        return true;
    }

    // Called when the shared deck runs dry and this player's unclaimed morto
    // becomes the new deck instead. Returns null if there's nothing to release.
    public List<Card> ReleaseUnusedMorto()
    {
        if (hasUsedMorto || morto.Count == 0) return null;
        List<Card> released = new List<Card>(morto);
        morto.Clear();
        hasUsedMorto = true; // no longer available for this player to pick up directly
        return released;
    }
}