using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

public class GameManager : MonoBehaviour
{
    public enum GameState
    {
        Setup,
        PlayerOneTurn, 
        PlayerTwoTurn,
        GameOver,
    }

    public enum TurnPhase
    {
        Draw,    // must draw from deck, or take the (unlocked) discard pile
        Meld,    // optional: lay down / extend melds
        Discard, // reserved for future use if you split "must discard" into its own explicit phase
    }

    [Header("Core References")] 
    public Deck deck;
    public Player playerOne;
    public Player playerTwo;
    public DiscardPile discardPile;
    
    [Header("Game State")]
    public GameState currentState = GameState.Setup;
    public TurnPhase currentPhase = TurnPhase.Draw;

    [Header("Scene Transition")]
    // Must exactly match the winner scene's file name and be added in
    // File -> Build Settings -> Scenes In Build.
    public string winnerSceneName = "WinnerScene";

    private Player activePlayer;
    private Player waitingPlayer;

    // Set when a player empties their hand via discard (indirect knockout):
    // they still get their morto, but by the rules can't act on it until their NEXT turn.
    // Not yet enforced against input - see note in StartTurn().
    private bool pendingIndirectMortoDelay;

    // Set true when a player's turn had nothing left to draw (deck and all
    // mortos exhausted). If this happens on two consecutive turns - i.e. neither
    // player could draw - the round ends. Reset to false on any successful draw.
    private bool previousTurnFailedToDraw;

    // --- Victory tracking ---
    // Set at the same moment the game ends, immediately before handing off
    // to the winner scene via GameResult (see GoToWinnerScene). Exposed here
    // too in case anything still active this same frame wants it.
    public Player WinningPlayer { get; private set; }

    void Start()
    {
        SetupGame();
    }

    void Update()
    {
        
    }

    private void SetupGame()
    {
        currentState = GameState.Setup;
        WinningPlayer = null;

        playerOne.DealStartingHand();
        playerTwo.DealStartingHand();

        playerOne.SetMorto(deck.DealMorto());
        playerTwo.SetMorto(deck.DealMorto());

        StartTurn(GameState.PlayerOneTurn);
    }

    private void StartTurn(GameState nextState)
    {
        currentState = nextState;
        currentPhase = TurnPhase.Draw;

        activePlayer = (currentState == GameState.PlayerOneTurn) ? playerOne : playerTwo;
        waitingPlayer = (currentState == GameState.PlayerOneTurn) ? playerTwo : playerOne;

        activePlayer.BeginTurn();
        waitingPlayer.EndTurn();

        // TODO: if pendingIndirectMortoDelay applies to activePlayer, this is where
        // you'd clear it now that their next turn has arrived (currently unused
        // beyond being set - hook it up once morto-use timing needs enforcing).

        Debug.Log($"{currentState} started!");
    }

    // ------------------------------------------------------------------
    // Draw phase
    // ------------------------------------------------------------------

    public void DrawFromDeck()
    {
        if (currentPhase != TurnPhase.Draw) return;

        Card drawn = deck.Deal();
        if (drawn == null)
        {
            if (TryRefillDeckFromMorto())
                drawn = deck.Deal();
        }

        if (drawn == null)
        {
            if (previousTurnFailedToDraw)
            {
                // Neither player could draw on consecutive turns - the stock
                // is truly dead. The round ends right here. Nobody went out,
                // so the winner (if any) is decided by score.
                EndGameByScore();
                return;
            }

            // This player can't draw, but it's the first time in a row this has
            // happened - skip the draw and let them meld/discard from their
            // existing hand instead. Their turn still ends normally via discard.
            previousTurnFailedToDraw = true;
            currentPhase = TurnPhase.Meld;
            Debug.Log("Nothing left to draw - skipping to meld/discard.");
            return;
        }

        previousTurnFailedToDraw = false; // a successful draw resets the streak
        HandleDrawnCard(drawn);
    }

    // When the deck runs dry, whichever morto hasn't been claimed yet becomes
    // the new deck. Tries playerOne's first, then playerTwo's.
    private bool TryRefillDeckFromMorto()
    {
        List<Card> released = playerOne.ReleaseUnusedMorto() ?? playerTwo.ReleaseUnusedMorto();
        if (released == null) return false;

        deck.RefillFrom(released);
        Debug.Log("Deck refilled from an unclaimed morto.");
        return true;
    }

    public void DrawFromDiscardPile()
    {
        if (currentPhase != TurnPhase.Draw) return;
        if (discardPile.IsLocked) return; // a discarded black three locks the pile

        // House rule: taking the pile is free - no requirement to immediately
        // meld the top card. The whole pile goes straight into the hand.
        List<Card> taken = discardPile.TakeAll();
        foreach (Card c in taken)
            activePlayer.AddCard(c);

        previousTurnFailedToDraw = false; // they found cards somewhere, streak broken
        currentPhase = TurnPhase.Meld;
    }

    private void HandleDrawnCard(Card drawn)
    {
        if (drawn.IsRedThree)
        {
            activePlayer.PlayRedThree(drawn);
            DrawFromDeck(); // red three is never kept - draw again to replace it
            return;
        }

        activePlayer.AddCard(drawn);
        currentPhase = TurnPhase.Meld;
    }

    // ------------------------------------------------------------------
    // Meld phase
    // ------------------------------------------------------------------

    public bool TryPlayMeld(List<Card> cards, Meld.MeldType type)
    {
        if (currentPhase != TurnPhase.Meld)
        {
            Debug.Log($"TryPlayMeld REJECTED: currentPhase is {currentPhase}, not Meld.");
            return false;
        }

        bool valid = (type == Meld.MeldType.Set)
            ? Meld.IsValidSet(cards)
            : Meld.IsValidRun(cards);

        if (!valid)
        {
            Debug.Log($"TryPlayMeld REJECTED: {type} validation failed for " +
                string.Join(", ", cards.Select(c => $"{c.rank} of {c.suit}")));
            return false;
        }

        // Would this meld empty the player's hand? (Assumes `cards` are all drawn
        // from hand, i.e. this is forming a brand-new meld, not extending one.)
        bool wouldEmptyHand = !activePlayer.Hand.Except(cards).Any();
        Debug.Log($"TryPlayMeld check: activePlayer has {activePlayer.Hand.Count} card(s) in hand, wouldEmptyHand={wouldEmptyHand}");

        if (wouldEmptyHand && !WouldBeLegalGoOut(activePlayer, cardsAboutToMeld: cards))
        {
            Debug.Log("TryPlayMeld REJECTED: would empty hand and WouldBeLegalGoOut returned false.");
            return false;
        }

        activePlayer.PlayMeld(cards, type);
        CheckHandEmptied(emptiedByDiscard: false);
        return true;
    }

    // A player may only end with an empty hand if they still have a morto to draw
    // into (so the round continues), or already have a natural canasta on the table
    // - counting the meld about to be played, since that meld could itself be the
    // natural canasta that legalizes going out.
    private bool WouldBeLegalGoOut(Player p, List<Card> cardsAboutToMeld)
    {
        if (p.HasMortoAvailable) return true;

        bool alreadyHasNatural = HasNaturalCanasta(p);
        bool thisMeldIsNatural = cardsAboutToMeld.TrueForAll(c => !c.IsWild) && cardsAboutToMeld.Count >= 7;

        return alreadyHasNatural || thisMeldIsNatural;
    }

    public bool TryExtendMeld(Player player, Meld meld, Card card)
    {
        if (currentPhase != TurnPhase.Meld) return false;
        if (player != activePlayer) return false;
        if (!player.TableMelds.Contains(meld)) return false;

        List<Card> proposed = new List<Card>(meld.cards) { card };
        bool valid = (meld.type == Meld.MeldType.Set)
            ? Meld.IsValidSet(proposed)
            : Meld.IsValidRun(proposed);

        if (!valid) return false;

        bool wouldEmptyHand = player.Hand.Count == 1 && player.Hand.Contains(card);
        if (wouldEmptyHand && !WouldBeLegalGoOut(player, cardsAboutToMeld: proposed))
        {
            return false;
        }

        player.ExtendMeld(meld, card);
        CheckHandEmptied(emptiedByDiscard: false);
        return true;
    }

    // ------------------------------------------------------------------
    // Discard - ends the turn
    // ------------------------------------------------------------------

    public bool DiscardCard(Card card)
    {
        // Must have drawn (and optionally melded) before discarding.
        if (currentPhase != TurnPhase.Meld) return false;

        bool wouldEmptyHand = activePlayer.Hand.Count == 1 && activePlayer.Hand.Contains(card);

        if (wouldEmptyHand && !WouldBeLegalGoOut(activePlayer, cardsAboutToMeld: new List<Card>()))
        {
            // Illegal: discarding this card would end their hand with no morto
            // left and no natural canasta on the table. Reject - they must hold
            // the card (or meld first) instead.
            return false;
        }

        activePlayer.RemoveCard(card);
        discardPile.Discard(card);

        bool wentOut = CheckHandEmptied(emptiedByDiscard: true);
        if (!wentOut)
        {
            EndCurrentTurn();
        }
        // If wentOut is true and the player had no morto left, CheckHandEmptied
        // already confirmed a natural canasta exists (guaranteed by the check
        // above) and set GameState.GameOver, so the turn simply doesn't advance.
        return true;
    }

    // Returns true if the active player's hand was empty after the triggering action.
    private bool CheckHandEmptied(bool emptiedByDiscard)
    {
        if (activePlayer.Hand.Count > 0) return false;

        if (activePlayer.TryPickUpMorto())
        {
            if (emptiedByDiscard)
            {
                // Indirect knockout: they get the morto, but by the rules can't use
                // it until their next turn. Turn still ends normally from here.
                pendingIndirectMortoDelay = true;
            }
            else
            {
                // Direct knockout: melded their last card, so they keep playing
                // this same turn with the morto cards now in hand.
                currentPhase = TurnPhase.Meld;
            }
        }
        else
        {
            // No morto left to take - this player has actually gone out.
            // TryPlayMeld/DiscardCard already guarantee a natural canasta exists
            // before letting this state occur, so this is a safety net, not the
            // primary enforcement point.
            string winnerName = (activePlayer == playerOne) ? "Player 1" : "Player 2";
            int winnerScore = TrancaScoring.ScorePlayer(activePlayer);
            int otherScore = TrancaScoring.ScorePlayer(activePlayer == playerOne ? playerTwo : playerOne);

            Debug.Log($"Game over - {winnerName} went out.");
            GoToWinnerScene(isTie: false, winnerName, winnerScore, otherScore, winner: activePlayer);
            return true; // GameManager (and this scene) is being torn down - nothing more to do
        }

        return true;
    }

    // Stock-exhaustion ending: nobody went out, so the winner is whoever has
    // the higher TrancaScoring total at the moment the stock died. An exact
    // tie is possible and is reported as such rather than picking one.
    private void EndGameByScore()
    {
        int p1Score = TrancaScoring.ScorePlayer(playerOne);
        int p2Score = TrancaScoring.ScorePlayer(playerTwo);

        if (p1Score == p2Score)
        {
            Debug.Log($"Round over - stock exhausted for both players. Tie at {p1Score} points each.");
            GoToWinnerScene(isTie: true, winnerName: null, winnerScore: p1Score, otherScore: p2Score, winner: null);
            return;
        }

        Player winner = p1Score > p2Score ? playerOne : playerTwo;
        string winnerName = (winner == playerOne) ? "Player 1" : "Player 2";
        int winnerScore = Mathf.Max(p1Score, p2Score);
        int otherScore = Mathf.Min(p1Score, p2Score);

        Debug.Log($"Round over - stock exhausted for both players. {winnerName} wins on points ({winnerScore} vs {otherScore}).");
        GoToWinnerScene(isTie: false, winnerName, winnerScore, otherScore, winner);
    }

    // Single exit point for ending the game: records the outcome in the
    // GameResult static (which survives the scene load) and hands off to the
    // dedicated winner scene. Set currentState first purely for anything that
    // might poll it in this same frame before the load takes effect; after
    // SceneManager.LoadScene runs, don't touch any other object on this
    // GameManager or its scene again - it's on its way out.
    private void GoToWinnerScene(bool isTie, string winnerName, int winnerScore, int otherScore, Player winner)
    {
        currentState = GameState.GameOver;
        WinningPlayer = winner;

        if (isTie)
            GameResult.SetTie(winnerScore);
        else
            GameResult.SetWinner(winnerName, winnerScore, otherScore);

        SceneManager.LoadScene(winnerSceneName);
    }

    private bool HasNaturalCanasta(Player p)
    {
        return p.TableMelds.Exists(m => m.IsCanasta && m.IsClean);
    }

    public void EndCurrentTurn()
    {
        if (currentState == GameState.GameOver) return;

        activePlayer.EndTurn();

        GameState next = (currentState == GameState.PlayerOneTurn)
            ? GameState.PlayerTwoTurn
            : GameState.PlayerOneTurn;

        StartTurn(next);
    }

    // ------------------------------------------------------------------
    // Debug hooks (Editor only) - jump straight to an end-game condition
    // without having to actually play out a hand. Right-click the
    // GameManager component in the Inspector (while in Play Mode) to fire
    // these from the context menu.
    // ------------------------------------------------------------------
#if UNITY_EDITOR
    [ContextMenu("DEBUG: Force Active Player To Go Out")]
    private void DebugForceActivePlayerGoOut()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("DebugForceActivePlayerGoOut only works in Play Mode.");
            return;
        }

        if (currentState == GameState.GameOver)
        {
            Debug.LogWarning("Already GameOver - nothing to force.");
            return;
        }

        // WouldBeLegalGoOut requires either an available morto or a natural
        // canasta already on the table. Fake a natural canasta directly onto
        // the active player's table state so the normal legality check passes
        // without needing 7 real matching cards drawn and melded.
        if (!HasNaturalCanasta(activePlayer))
        {
            List<Card> fakeCanasta = activePlayer.Hand.Take(7).ToList();
            if (fakeCanasta.Count < 7)
            {
                Debug.LogWarning($"DebugForceActivePlayerGoOut: active player only has " +
                    $"{activePlayer.Hand.Count} card(s) in hand, need at least 7 to fake a natural " +
                    "canasta this way. Draw a few more cards first, or just clear morto manually.");
                return;
            }
            activePlayer.PlayMeld(fakeCanasta, Meld.MeldType.Set);
        }

        // Empty whatever's left in hand - RemoveCard mutates the list we're
        // iterating, so drain by always taking index 0 rather than foreach.
        while (activePlayer.Hand.Count > 0)
            activePlayer.RemoveCard(activePlayer.Hand[0]);

        Debug.Log($"DEBUG: forced {(activePlayer == playerOne ? "Player 1" : "Player 2")} to go out.");
        CheckHandEmptied(emptiedByDiscard: false);
    }

    [ContextMenu("DEBUG: Force Stock Exhaustion (score-based ending)")]
    private void DebugForceStockExhaustion()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("DebugForceStockExhaustion only works in Play Mode.");
            return;
        }

        if (currentState == GameState.GameOver)
        {
            Debug.LogWarning("Already GameOver - nothing to force.");
            return;
        }

        if (currentPhase != TurnPhase.Draw)
        {
            Debug.LogWarning($"DebugForceStockExhaustion: currentPhase is {currentPhase}, not Draw. " +
                "This hook drains the deck and calls DrawFromDeck(), which no-ops outside the Draw phase.");
            return;
        }

        while (deck.Deal() != null) { /* drain it */ }

        // Also strip both mortos so TryRefillDeckFromMorto() can't quietly
        // refill the deck we just drained.
        activePlayer.ReleaseUnusedMorto();
        waitingPlayer.ReleaseUnusedMorto();

        // First call hits the "nothing to draw, skip to meld" branch and sets
        // previousTurnFailedToDraw; simulate the flag being already set so
        // this single call goes straight to the score-based ending.
        previousTurnFailedToDraw = true;
        Debug.Log("DEBUG: forcing stock exhaustion - calling DrawFromDeck() now.");
        DrawFromDeck();
    }

    [ContextMenu("DEBUG: Log Current Scores")]
    private void DebugLogScores()
    {
        if (!Application.isPlaying) return;
        Debug.Log($"Player 1: {TrancaScoring.ScorePlayer(playerOne)} pts | " +
                   $"Player 2: {TrancaScoring.ScorePlayer(playerTwo)} pts");
    }
#endif
}