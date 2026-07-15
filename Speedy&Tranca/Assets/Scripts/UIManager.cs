using UnityEngine;
using TMPro;

// Attach this to an empty GameObject under your Canvas (e.g. "UIManager").
// It polls GameManager each frame and refreshes the score/turn labels.
// Polling (rather than firing events from GameManager) is the simplest
// approach here since scores only change a handful of times per turn and
// TrancaScoring.ScorePlayer is a cheap O(cards) sum - no need to wire event
// callbacks through Player/GameManager for this.
public class UIManager : MonoBehaviour
{
    [Header("Core References")]
    public GameManager gameManager;

    [Header("Score Labels")]
    public TMP_Text playerOneScoreText;
    public TMP_Text playerTwoScoreText;

    [Header("Turn Label")]
    public TMP_Text turnIndicatorText;

    // Optional: tint the active player's score label to make the turn more
    // visually obvious. Leave both colors the same if you don't want this.
    [Header("Optional Active-Player Highlight")]
    public bool highlightActivePlayer = true;
    public Color activeColor = Color.yellow;
    public Color inactiveColor = Color.white;

    void Update()
    {
        if (gameManager == null) return;

        RefreshScores();
        RefreshTurnIndicator();
    }

    private void RefreshScores()
    {
        if (playerOneScoreText != null && gameManager.playerOne != null)
            playerOneScoreText.text = $"Player 1: {TrancaScoring.ScorePlayer(gameManager.playerOne)}";

        if (playerTwoScoreText != null && gameManager.playerTwo != null)
            playerTwoScoreText.text = $"Player 2: {TrancaScoring.ScorePlayer(gameManager.playerTwo)}";

        if (!highlightActivePlayer) return;

        bool p1Active = gameManager.currentState == GameManager.GameState.PlayerOneTurn;
        bool p2Active = gameManager.currentState == GameManager.GameState.PlayerTwoTurn;

        if (playerOneScoreText != null)
            playerOneScoreText.color = p1Active ? activeColor : inactiveColor;
        if (playerTwoScoreText != null)
            playerTwoScoreText.color = p2Active ? activeColor : inactiveColor;
    }

    private void RefreshTurnIndicator()
    {
        if (turnIndicatorText == null) return;

        switch (gameManager.currentState)
        {
            case GameManager.GameState.PlayerOneTurn:
                turnIndicatorText.text = $"Player 1's turn - {PhaseLabel()}";
                break;
            case GameManager.GameState.PlayerTwoTurn:
                turnIndicatorText.text = $"Player 2's turn - {PhaseLabel()}";
                break;
            case GameManager.GameState.Setup:
                turnIndicatorText.text = "Dealing...";
                break;
            case GameManager.GameState.GameOver:
                turnIndicatorText.text = "Game Over";
                break;
        }
    }

    private string PhaseLabel()
    {
        switch (gameManager.currentPhase)
        {
            case GameManager.TurnPhase.Draw: return "Draw a card";
            case GameManager.TurnPhase.Meld: return "Meld or discard";
            case GameManager.TurnPhase.Discard: return "Discard";
            default: return "";
        }
    }

}