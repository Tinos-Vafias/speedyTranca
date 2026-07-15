using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Attach to an empty GameObject in the dedicated winner/victory scene.
// GameManager loads this scene when the game ends; this script just reads
// the static GameResult that GameManager filled in right before the load,
// displays it, and sends the player back to the main scene on button press.
public class WinnerSceneController : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text messageText;
    public Button playAgainButton;

    [Header("Scene To Return To")]
    // Must exactly match the main scene's file name and be added in
    // File -> Build Settings -> Scenes In Build.
    public string mainSceneName = "MainScene";

    void Awake()
    {
        if (playAgainButton != null)
            playAgainButton.onClick.AddListener(ReturnToMainScene);
    }

    void Start()
    {
        if (messageText != null)
            messageText.text = BuildMessage();
    }

    private string BuildMessage()
    {
        if (!GameResult.HasResult)
        {
            // Defensive fallback - shouldn't happen if this scene is only
            // ever reached via GameManager's end-of-game transition.
            return "Game Over";
        }

        if (GameResult.IsTie)
            return $"It's a tie!\nBoth players finished with {GameResult.WinnerScore} points.";

        return $"Congratulations, {GameResult.WinnerName}!\nFinal score: {GameResult.WinnerScore} points"
               + $"\n(Other player: {GameResult.OtherScore} points)";
    }

    private void ReturnToMainScene()
    {
        GameResult.Clear();
        SceneManager.LoadScene(mainSceneName);
    }
}