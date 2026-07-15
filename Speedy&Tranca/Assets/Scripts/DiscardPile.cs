using System.Collections.Generic;
using UnityEngine;

public class DiscardPile : MonoBehaviour
{
    private List<Card> pile = new List<Card>();
    public bool IsLocked { get; private set; }
    public Transform pileAnchor;

    public Card TopCard => pile.Count > 0 ? pile[pile.Count - 1] : null;

    public void Discard(Card card)
    {
        card.owner = null; // no longer belongs to any player until someone takes the pile
        pile.Add(card);
        card.gameObject.SetActive(true);
        card.transform.position = pileAnchor.position + Vector3.forward * -pile.Count * 0.02f;
        IsLocked = card.IsBlackThree;
    }

    public List<Card> TakeAll()
    {
        var taken = new List<Card>(pile);
        pile.Clear();
        IsLocked = false;
        return taken;
    }
}