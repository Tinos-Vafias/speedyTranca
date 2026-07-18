using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using UnityEngine.XR;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class Player : NetworkBehaviour
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

    // handContainer/meldContainer/redThreeContainer/stagingZoneAnchor are now
    // computed properties (below), not cached fields - each one reads IsOwner
    // fresh, every time it's used. Wire the SAME four pairs of anchors on BOTH
    // the Player One and Player Two GameObjects - whichever Player object
    // belongs to the local client uses the "local" anchor (bottom half of the
    // screen), the other one uses the "opponent" anchor, regardless of whether
    // that's Player One or Player Two underneath.
    [Header("Local Player Anchors (bottom half)")]
    public Transform localHandAnchor;
    public Transform localMeldAnchor;
    public Transform localRedThreeAnchor;
    public Transform localStagingAnchor;

    [Header("Opponent Anchors (top half)")]
    public Transform opponentHandAnchor;
    public Transform opponentMeldAnchor;
    public Transform opponentRedThreeAnchor;
    public Transform opponentStagingAnchor;

    // IsOwner is always live/current - computing these on every access means
    // there's no cached value that can go stale, and no dependency on
    // OnGainedOwnership/OnNetworkSpawn firing in any particular order.
    private Transform HandContainer => IsOwner ? localHandAnchor : opponentHandAnchor;
    private Transform MeldContainer => IsOwner ? localMeldAnchor : opponentMeldAnchor;
    private Transform RedThreeContainer => IsOwner ? localRedThreeAnchor : opponentRedThreeAnchor;
    private Transform StagingZoneAnchor => IsOwner ? localStagingAnchor : opponentStagingAnchor;

    // Whose turn it is is decided by the server (via GameManager); this just
    // mirrors that decision so every client can read IsMyTurn correctly.
    private NetworkVariable<bool> netIsMyTurn = new NetworkVariable<bool>(false);
    public bool IsMyTurn => netIsMyTurn.Value;

    // --- Melds / table state ---
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

    void Start()
    {
        numCards = hand.Count;
    }

    public void generateHand()
    {
        if (!IsServer) return; // dealing is server-only

        for (int i = 0; i < 11; i++)
        {
            Card dealt = deck.Deal();
            dealt.owner = this;
            hand.Add(dealt);
            dealt.SetVisible(true);
        }
        numCards = hand.Count;
        DisplayHand();

        // Mirror the deal to every other client.
        var refs = hand.Select(c => (NetworkObjectReference)c.NetworkObject).ToArray();
        MirrorInitialHandClientRpc(refs);
    }

    [ClientRpc]
    private void MirrorInitialHandClientRpc(NetworkObjectReference[] cardRefs)
    {
        if (IsServer) return;
        foreach (var r in cardRefs)
        {
            if (r.TryGet(out NetworkObject obj))
            {
                Card c = obj.GetComponent<Card>();
                c.owner = this;
                hand.Add(c);
            }
        }
        DisplayHand();
    }

    void DisplayHand()
    {
        foreach (Card card in hand)
            card.gameObject.SetActive(false);

        int count = hand.Count;
        float fanAngle = 30f;
        float radius = 15f;

        Debug.Log($"[Player:{gameObject.name}] DisplayHand - IsOwner={IsOwner}, IsServer={IsServer}, " +
            $"OwnerClientId={OwnerClientId}, LocalClientId={NetworkManager.Singleton.LocalClientId}, " +
            $"HandContainer={(HandContainer == null ? "NULL" : HandContainer.name)}, cardCount={count}");

        Vector3 arcCenter = HandContainer.position + new Vector3(0, -radius + 2f, 0);

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

    // Plain local mutation - called directly on whichever machine needs to
    // apply the change (server applies it live, other clients apply it from
    // inside a Mirror*ClientRpc handler below).
    public void RemoveCard(Card card)
    {
        hand.Remove(card);
        DisplayHand();
    }

    public void AddCard(Card card)
    {
        hand.Add(card);
        DisplayHand();
        card.owner = this;
    }

    // --- Server-authoritative wrappers: mutate locally AND replicate ---

    public void RemoveCardAndSync(Card card)
    {
        RemoveCard(card);
        MirrorRemoveCardClientRpc(card.NetworkObject);
    }

    [ClientRpc]
    private void MirrorRemoveCardClientRpc(NetworkObjectReference cardRef)
    {
        if (IsServer) return;
        if (cardRef.TryGet(out NetworkObject obj)) RemoveCard(obj.GetComponent<Card>());
    }

    public void AddCardAndSync(Card card)
    {
        AddCard(card);
        card.SetVisible(true);
        MirrorAddCardClientRpc(card.NetworkObject);
    }

    [ClientRpc]
    private void MirrorAddCardClientRpc(NetworkObjectReference cardRef)
    {
        if (IsServer) return;
        if (cardRef.TryGet(out NetworkObject obj)) AddCard(obj.GetComponent<Card>());
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
            DisplayStaging();
            return;
        }

        stagingMeld.Remove(card);
        hand.Add(card);
        DisplayHand();
        DisplayStaging();
    }

    // --- Discard: request goes to the server, result comes back via the
    // shared RejectActionClientRpc (on failure) or the mirrored hand/pile
    // updates (on success). ---

    private void TryDiscard(Card card)
    {
        RequestDiscardServerRpc(card.NetworkObject);
    }

    [ServerRpc]
    private void RequestDiscardServerRpc(NetworkObjectReference cardRef)
    {
        if (gameManager.ActivePlayer != this) { RejectActionClientRpc(); return; }
        if (!cardRef.TryGet(out NetworkObject cardObj)) return;
        Card card = cardObj.GetComponent<Card>();

        bool success = gameManager.DiscardCard(card);
        if (!success) RejectActionClientRpc();
    }

    // Generic "that didn't work, snap back to hand" notice. Safe to reuse
    // for every action type below - only the requesting client's copy of
    // this Player object actually reacts to it.
    [ClientRpc]
    private void RejectActionClientRpc()
    {
        if (!IsOwner) return;
        DisplayHand();
    }

    // --- Draw ---

    [ServerRpc]
    public void RequestDrawFromDeckServerRpc()
    {
        if (gameManager.ActivePlayer != this) { RejectActionClientRpc(); return; }
        gameManager.DrawFromDeck();
    }

    [ServerRpc]
    public void RequestDrawFromDiscardServerRpc()
    {
        if (gameManager.ActivePlayer != this) { RejectActionClientRpc(); return; }
        gameManager.DrawFromDiscardPile();
    }

    public void RequestTakeDiscardPile()
    {
        RequestDrawFromDiscardServerRpc();
    }

    // --- Staging a brand-new meld (all speculative and purely local until
    // the group is complete - nothing here needs to be networked until
    // CommitStaging actually asks the server to play it). ---

    private void TryAddToStaging(Card card)
    {
        List<Card> proposed = new List<Card>(stagingMeld) { card };

        bool compatibleAsSet = PartialSetCompatible(proposed);
        bool compatibleAsRun = PartialRunCompatible(proposed);

        if (!compatibleAsSet && !compatibleAsRun)
        {
            Debug.Log($"Staging: {card.rank} of {card.suit} rejected - doesn't fit as a set or run with the current staged group.");
            DisplayHand();
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
        var refs = toCommit.Select(c => (NetworkObjectReference)c.NetworkObject).ToArray();
        RequestPlayMeldServerRpc(refs, type);
        // Don't clear stagingMeld yet - MirrorPlayMeldClientRpc clears it for
        // us once the server confirms (see below). On rejection it just stays
        // staged, same as the original synchronous behavior.
    }

    [ServerRpc]
    private void RequestPlayMeldServerRpc(NetworkObjectReference[] cardRefs, Meld.MeldType type)
    {
        if (gameManager.ActivePlayer != this) { RejectActionClientRpc(); return; }

        List<Card> cards = new List<Card>();
        foreach (var r in cardRefs)
            if (r.TryGet(out NetworkObject obj)) cards.Add(obj.GetComponent<Card>());

        bool success = gameManager.TryPlayMeld(cards, type);
        if (!success) RejectActionClientRpc();
    }

    public void PlayMeldAndSync(List<Card> cards, Meld.MeldType type)
    {
        PlayMeld(cards, type);
        var refs = cards.Select(c => (NetworkObjectReference)c.NetworkObject).ToArray();
        MirrorPlayMeldClientRpc(refs, type);
    }

    [ClientRpc]
    private void MirrorPlayMeldClientRpc(NetworkObjectReference[] cardRefs, Meld.MeldType type)
    {
        if (IsServer) return;
        var cards = new List<Card>();
        foreach (var r in cardRefs)
            if (r.TryGet(out NetworkObject obj)) cards.Add(obj.GetComponent<Card>());
        PlayMeld(cards, type);
        if (IsOwner) stagingMeld.Clear(); // this client's own staged group just got committed
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
        return nonWild.Select(c => c.suit).Distinct().Count() <= 1;
    }

    private void DisplayStaging()
    {
        for (int i = 0; i < stagingMeld.Count; i++)
        {
            stagingMeld[i].gameObject.SetActive(true);
            stagingMeld[i].transform.position = StagingZoneAnchor.position
                + new Vector3(i * cardSpacingWithinMeld, 0, 0);
            stagingMeld[i].GetComponent<SpriteRenderer>().sortingOrder = i;
        }
    }

    // --- Extending an existing meld ---

    private void TryExtendMeld(Meld meld, Card card)
    {
        int meldIndex = tableMelds.IndexOf(meld);
        RequestExtendMeldServerRpc(meldIndex, card.NetworkObject);
    }

    [ServerRpc]
    private void RequestExtendMeldServerRpc(int meldIndex, NetworkObjectReference cardRef)
    {
        if (gameManager.ActivePlayer != this) { RejectActionClientRpc(); return; }
        if (meldIndex < 0 || meldIndex >= tableMelds.Count) { RejectActionClientRpc(); return; }
        if (!cardRef.TryGet(out NetworkObject cardObj)) return;
        Card card = cardObj.GetComponent<Card>();

        bool success = gameManager.TryExtendMeld(this, tableMelds[meldIndex], card);
        if (!success) RejectActionClientRpc();
    }

    public void ExtendMeldAndSync(Meld meld, Card card)
    {
        int meldIndex = tableMelds.IndexOf(meld);
        ExtendMeld(meld, card);
        MirrorExtendMeldClientRpc(meldIndex, card.NetworkObject);
    }

    [ClientRpc]
    private void MirrorExtendMeldClientRpc(int meldIndex, NetworkObjectReference cardRef)
    {
        if (IsServer) return;
        if (meldIndex < 0 || meldIndex >= tableMelds.Count) return;
        if (cardRef.TryGet(out NetworkObject obj)) ExtendMeld(tableMelds[meldIndex], obj.GetComponent<Card>());
    }

    public void ExtendMeld(Meld meld, Card card)
    {
        hand.Remove(card);
        meld.cards.Add(card);
        DisplayHand();
        DisplayMelds();
    }

    public void BeginTurn()
    {
        if (IsServer) netIsMyTurn.Value = true;
    }

    public void EndTurn()
    {
        if (IsServer) netIsMyTurn.Value = false;
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
        foreach (var go in meldZoneObjects) Destroy(go);
        meldZoneObjects.Clear();

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
                    meld.cards[i].transform.position = MeldContainer.position
                        + new Vector3(cursorX + i * cardSpacingWithinMeld, y, 0);
                    meld.cards[i].GetComponent<SpriteRenderer>().sortingOrder = i;
                }

                GameObject zoneObj = new GameObject($"MeldZone_{tableMelds.IndexOf(meld)}");
                zoneObj.transform.SetParent(MeldContainer);
                zoneObj.transform.position = MeldContainer.position
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
        card.transform.position = RedThreeContainer.position + new Vector3(playedRedThrees.Count * 0.6f, 0, 0);
    }

    public void PlayRedThreeAndSync(Card card)
    {
        PlayRedThree(card);
        card.SetVisible(true);
        MirrorPlayRedThreeClientRpc(card.NetworkObject);
    }

    [ClientRpc]
    private void MirrorPlayRedThreeClientRpc(NetworkObjectReference cardRef)
    {
        if (IsServer) return;
        if (cardRef.TryGet(out NetworkObject obj)) PlayRedThree(obj.GetComponent<Card>());
    }

    // --- Morto pickup ---

    public void SetMorto(List<Card> mortoCards)
    {
        morto = mortoCards;
    }

    public bool HasMortoAvailable => !hasUsedMorto && morto.Count > 0;

    // Returns the cards picked up (for GameManager to mirror to other
    // clients), or null if there was nothing to pick up.
    public List<Card> TryPickUpMorto()
    {
        if (hasUsedMorto || morto.Count == 0) return null;

        List<Card> picked = new List<Card>(morto);
        foreach (var c in picked)
        {
            c.owner = this;
            hand.Add(c);
        }
        morto.Clear();
        hasUsedMorto = true;
        DisplayHand();
        return picked;
    }

    public void MortoPickupAndSync(List<Card> pickedUp)
    {
        var refs = pickedUp.Select(c => (NetworkObjectReference)c.NetworkObject).ToArray();
        MirrorMortoPickupClientRpc(refs);
    }

    [ClientRpc]
    private void MirrorMortoPickupClientRpc(NetworkObjectReference[] cardRefs)
    {
        if (IsServer) return;
        foreach (var r in cardRefs)
        {
            if (r.TryGet(out NetworkObject obj))
            {
                Card c = obj.GetComponent<Card>();
                c.owner = this;
                hand.Add(c);
            }
        }
        DisplayHand();
    }

    public List<Card> ReleaseUnusedMorto()
    {
        if (hasUsedMorto || morto.Count == 0) return null;
        List<Card> released = new List<Card>(morto);
        morto.Clear();
        hasUsedMorto = true;
        return released;
    }
}