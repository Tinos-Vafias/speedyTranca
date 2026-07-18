using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;

public class GameManager : NetworkBehaviour
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
        Draw,
        Meld,
        Discard,
    }

    [Header("Core References")]
    public Deck deck;
    public Player playerOne;
    public Player playerTwo;
    public DiscardPile discardPile;

    [Header("Game State")]
    // These used to be plain fields. They're now backed by NetworkVariables
    // so every connected client - not just the host - sees the true current
    // state (UIManager and Player's phase check both just read these).
    private NetworkVariable<GameState> netCurrentState = new NetworkVariable<GameState>(GameState.Setup);
    private NetworkVariable<TurnPhase> netCurrentPhase = new NetworkVariable<TurnPhase>(TurnPhase.Draw);
    public GameState currentState => netCurrentState.Value;
    public TurnPhase currentPhase => netCurrentPhase.Value;

    [Header("Scene Transition")]
    public string winnerSceneName = "WinnerScene";

    private Player activePlayer;
    private Player waitingPlayer;
    public Player ActivePlayer => activePlayer;

    // Whichever Player object the LOCAL client owns - null until
    // NetworkGameSetup has assigned ownership (or if this machine is
    // spectating, which isn't wired up in this project).
    public Player LocalPlayer
    {
        get
        {
            if (playerOne != null && playerOne.IsOwner) return playerOne;
            if (playerTwo != null && playerTwo.IsOwner) return playerTwo;
            return null;
        }
    }

    private bool pendingIndirectMortoDelay;
    private bool previousTurnFailedToDraw;

    public Player WinningPlayer { get; private set; }

    // SetupGame used to run from Start(). It now waits for
    // NetworkGameSetup.BeginGame() to fire once both players are connected
    // and assigned, so the round doesn't start before Player Two has joined.
    public void BeginGame()
    {
        if (!IsServer) return;
        SetupGame();
    }

    private void SetupGame()
    {
        netCurrentState.Value = GameState.Setup;
        WinningPlayer = null;

        playerOne.DealStartingHand();
        playerTwo.DealStartingHand();

        playerOne.SetMorto(deck.DealMorto());
        playerTwo.SetMorto(deck.DealMorto());

        StartTurn(GameState.PlayerOneTurn);
    }

    private void StartTurn(GameState nextState)
    {
        if (!IsServer) return;

        netCurrentState.Value = nextState;
        netCurrentPhase.Value = TurnPhase.Draw;

        activePlayer = (nextState == GameState.PlayerOneTurn) ? playerOne : playerTwo;
        waitingPlayer = (nextState == GameState.PlayerOneTurn) ? playerTwo : playerOne;

        activePlayer.BeginTurn();
        waitingPlayer.EndTurn();

        Debug.Log($"{nextState} started!");
    }

    // ------------------------------------------------------------------
    // Draw phase - called only from Player's RequestDrawFromDeckServerRpc /
    // RequestDrawFromDiscardServerRpc, both of which already verified the
    // caller is the active player before getting here.
    // ------------------------------------------------------------------

    public void DrawFromDeck()
    {
        if (!IsServer) return;
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
                EndGameByScore();
                return;
            }

            previousTurnFailedToDraw = true;
            netCurrentPhase.Value = TurnPhase.Meld;
            Debug.Log("Nothing left to draw - skipping to meld/discard.");
            return;
        }

        previousTurnFailedToDraw = false;
        HandleDrawnCard(drawn);
    }

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
        if (!IsServer) return;
        if (currentPhase != TurnPhase.Draw) return;
        if (discardPile.IsLocked) return;

        List<Card> taken = discardPile.TakeAllAndSync();
        foreach (Card c in taken)
            activePlayer.AddCardAndSync(c);

        previousTurnFailedToDraw = false;
        netCurrentPhase.Value = TurnPhase.Meld;
    }

    private void HandleDrawnCard(Card drawn)
    {
        if (drawn.IsRedThree)
        {
            activePlayer.PlayRedThreeAndSync(drawn);
            DrawFromDeck();
            return;
        }

        activePlayer.AddCardAndSync(drawn);
        netCurrentPhase.Value = TurnPhase.Meld;
    }

    // ------------------------------------------------------------------
    // Meld phase - called only from Player's Request*ServerRpc methods.
    // ------------------------------------------------------------------

    public bool TryPlayMeld(List<Card> cards, Meld.MeldType type)
    {
        if (!IsServer) return false;
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

        bool wouldEmptyHand = !activePlayer.Hand.Except(cards).Any();
        Debug.Log($"TryPlayMeld check: activePlayer has {activePlayer.Hand.Count} card(s) in hand, wouldEmptyHand={wouldEmptyHand}");

        if (wouldEmptyHand && !WouldBeLegalGoOut(activePlayer, cardsAboutToMeld: cards))
        {
            Debug.Log("TryPlayMeld REJECTED: would empty hand and WouldBeLegalGoOut returned false.");
            return false;
        }

        activePlayer.PlayMeldAndSync(cards, type);
        CheckHandEmptied(emptiedByDiscard: false);
        return true;
    }

    private bool WouldBeLegalGoOut(Player p, List<Card> cardsAboutToMeld)
    {
        if (p.HasMortoAvailable) return true;

        bool alreadyHasNatural = HasNaturalCanasta(p);
        bool thisMeldIsNatural = cardsAboutToMeld.TrueForAll(c => !c.IsWild) && cardsAboutToMeld.Count >= 7;

        return alreadyHasNatural || thisMeldIsNatural;
    }

    public bool TryExtendMeld(Player player, Meld meld, Card card)
    {
        if (!IsServer) return false;
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

        player.ExtendMeldAndSync(meld, card);
        CheckHandEmptied(emptiedByDiscard: false);
        return true;
    }

    // ------------------------------------------------------------------
    // Discard - ends the turn. Called only from Player's RequestDiscardServerRpc.
    // ------------------------------------------------------------------

    public bool DiscardCard(Card card)
    {
        if (!IsServer) return false;
        if (currentPhase != TurnPhase.Meld) return false;

        bool wouldEmptyHand = activePlayer.Hand.Count == 1 && activePlayer.Hand.Contains(card);

        if (wouldEmptyHand && !WouldBeLegalGoOut(activePlayer, cardsAboutToMeld: new List<Card>()))
        {
            return false;
        }

        activePlayer.RemoveCardAndSync(card);
        discardPile.DiscardAndSync(card);

        bool wentOut = CheckHandEmptied(emptiedByDiscard: true);
        if (!wentOut)
        {
            EndCurrentTurn();
        }
        return true;
    }

    private bool CheckHandEmptied(bool emptiedByDiscard)
    {
        if (activePlayer.Hand.Count > 0) return false;

        List<Card> pickedUp = activePlayer.TryPickUpMorto();
        if (pickedUp != null)
        {
            activePlayer.MortoPickupAndSync(pickedUp);

            if (emptiedByDiscard)
            {
                pendingIndirectMortoDelay = true;
            }
            else
            {
                netCurrentPhase.Value = TurnPhase.Meld;
            }
        }
        else
        {
            string winnerName = (activePlayer == playerOne) ? "Player 1" : "Player 2";
            int winnerScore = TrancaScoring.ScorePlayer(activePlayer);
            int otherScore = TrancaScoring.ScorePlayer(activePlayer == playerOne ? playerTwo : playerOne);

            Debug.Log($"Game over - {winnerName} went out.");
            GoToWinnerScene(isTie: false, winnerName, winnerScore, otherScore, winner: activePlayer);
            return true;
        }

        return true;
    }

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

    // Every machine needs to end up with the same GameResult static data
    // before the scene actually switches, since GameResult itself is just a
    // local, per-process cache - see AnnounceResultClientRpc.
    private void GoToWinnerScene(bool isTie, string winnerName, int winnerScore, int otherScore, Player winner)
    {
        if (!IsServer) return;

        netCurrentState.Value = GameState.GameOver;
        WinningPlayer = winner;

        AnnounceResultClientRpc(isTie, winnerName ?? "", winnerScore, otherScore);

        if (isTie) GameResult.SetTie(winnerScore);
        else GameResult.SetWinner(winnerName, winnerScore, otherScore);

        // NGO's own scene manager, not SceneManager.LoadScene directly - this
        // is what makes both machines load the winner scene together.
        // Requires "Enable Scene Management" on the NetworkManager.
        NetworkManager.Singleton.SceneManager.LoadScene(winnerSceneName, LoadSceneMode.Single);
    }

    [ClientRpc]
    private void AnnounceResultClientRpc(bool isTie, string winnerName, int winnerScore, int otherScore)
    {
        if (IsServer) return; // host already set this directly above
        if (isTie) GameResult.SetTie(winnerScore);
        else GameResult.SetWinner(winnerName, winnerScore, otherScore);
    }

    private bool HasNaturalCanasta(Player p)
    {
        return p.TableMelds.Exists(m => m.IsCanasta && m.IsClean);
    }

    public void EndCurrentTurn()
    {
        if (!IsServer) return;
        if (currentState == GameState.GameOver) return;

        activePlayer.EndTurn();

        GameState next = (currentState == GameState.PlayerOneTurn)
            ? GameState.PlayerTwoTurn
            : GameState.PlayerOneTurn;

        StartTurn(next);
    }

    // ------------------------------------------------------------------
    // Debug hooks (Editor only) - now server-only, since game state can
    // only legally be mutated on the server.
    // ------------------------------------------------------------------
#if UNITY_EDITOR
    [ContextMenu("DEBUG: Force Active Player To Go Out")]
    private void DebugForceActivePlayerGoOut()
    {
        if (!Application.isPlaying || !IsServer)
        {
            Debug.LogWarning("DebugForceActivePlayerGoOut only works in Play Mode, on the server/host.");
            return;
        }

        if (currentState == GameState.GameOver)
        {
            Debug.LogWarning("Already GameOver - nothing to force.");
            return;
        }

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
            activePlayer.PlayMeldAndSync(fakeCanasta, Meld.MeldType.Set);
        }

        while (activePlayer.Hand.Count > 0)
            activePlayer.RemoveCardAndSync(activePlayer.Hand[0]);

        Debug.Log($"DEBUG: forced {(activePlayer == playerOne ? "Player 1" : "Player 2")} to go out.");
        CheckHandEmptied(emptiedByDiscard: false);
    }

    [ContextMenu("DEBUG: Force Stock Exhaustion (score-based ending)")]
    private void DebugForceStockExhaustion()
    {
        if (!Application.isPlaying || !IsServer)
        {
            Debug.LogWarning("DebugForceStockExhaustion only works in Play Mode, on the server/host.");
            return;
        }

        if (currentState == GameState.GameOver)
        {
            Debug.LogWarning("Already GameOver - nothing to force.");
            return;
        }

        if (currentPhase != TurnPhase.Draw)
        {
            Debug.LogWarning($"DebugForceStockExhaustion: currentPhase is {currentPhase}, not Draw.");
            return;
        }

        while (deck.Deal() != null) { /* drain it */ }

        activePlayer.ReleaseUnusedMorto();
        waitingPlayer.ReleaseUnusedMorto();

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