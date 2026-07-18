using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

public class DiscardPile : NetworkBehaviour
{
    private List<Card> pile = new List<Card>();
    public bool IsLocked { get; private set; }
    public Transform pileAnchor;

    public Card TopCard => pile.Count > 0 ? pile[pile.Count - 1] : null;

    // Plain local mutation - called directly on whichever machine needs to
    // apply the change.
    private void Discard(Card card)
    {
        card.owner = null;
        pile.Add(card);
        card.transform.position = pileAnchor.position + Vector3.forward * -pile.Count * 0.02f;
        IsLocked = card.IsBlackThree;
    }

    private List<Card> TakeAll()
    {
        var taken = new List<Card>(pile);
        pile.Clear();
        IsLocked = false;
        return taken;
    }

    // --- Server-authoritative wrappers ---

    public void DiscardAndSync(Card card)
    {
        Discard(card);
        card.SetVisible(true);
        MirrorDiscardClientRpc(card.NetworkObject);
    }

    public List<Card> TakeAllAndSync()
    {
        List<Card> taken = TakeAll();
        var refs = taken.Select(c => (NetworkObjectReference)c.NetworkObject).ToArray();
        MirrorTakeAllClientRpc(refs);
        return taken;
    }

    [ClientRpc]
    private void MirrorDiscardClientRpc(NetworkObjectReference cardRef)
    {
        if (IsServer) return;
        if (cardRef.TryGet(out NetworkObject obj)) Discard(obj.GetComponent<Card>());
    }

    [ClientRpc]
    private void MirrorTakeAllClientRpc(NetworkObjectReference[] cardRefs)
    {
        if (IsServer) return;
        // The cards themselves get re-parented into a hand via each Player's
        // own mirrored AddCard call - this just clears the pile bookkeeping.
        pile.Clear();
        IsLocked = false;
    }
}