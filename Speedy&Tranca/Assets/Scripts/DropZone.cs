using UnityEngine;

// Attach to a collider on any table region a card can be dropped onto.
// Card.OnMouseUp looks for one of these under the release point.
public class DropZone : MonoBehaviour
{
    public enum ZoneType
    {
        Hand,           // a player's own hand area - dropping here just returns the card to hand
        DiscardPile,    // the shared discard pile
        NewMeldStaging, // a player's single staging area for building a brand-new meld
        ExistingMeld,   // NOT YET WIRED - see note in Player.OnCardDragEnd
    }

    public ZoneType zoneType;

    // For Hand / NewMeldStaging zones, whichever Player this zone belongs to.
    // Leave null for the shared DiscardPile zone.
    public Player owner;

    // Only relevant for ExistingMeld zones (unused for now).
    [HideInInspector] public Meld linkedMeld;
}