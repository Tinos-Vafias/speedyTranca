using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

// Attach to an empty GameObject in the dedicated winner/victory scene.
// GameManager loads this scene (via NetworkManager.SceneManager, so it loads
// on both machines together) and announces the result via ClientRpc just
// before doing so - see GameManager.GoToWinnerScene / AnnounceResultClientRpc.
public class WinnerSceneController : NetworkBehaviour
{
    [Header("UI")]
    public TMP_Text messageText;
    public Button playAgainButton;

    [Header("Scene To Return To")]
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

        // Only the host actually owns scene transitions - a non-host client
        // pressing "Play Again" shouldn't be able to yank both machines back
        // to the main scene on their own.
        if (playAgainButton != null && NetworkManager.Singleton != null)
            playAgainButton.interactable = NetworkManager.Singleton.IsServer;
    }

    private string BuildMessage()
    {
        if (!GameResult.HasResult)
        {
            return "Game Over";
        }

        if (GameResult.IsTie)
            return $"It's a tie!\nBoth players finished with {GameResult.WinnerScore} points.";

        return $"Congratulations, {GameResult.WinnerName}!\nFinal score: {GameResult.WinnerScore} points"
               + $"\n(Other player: {GameResult.OtherScore} points)";
    }

    private void ReturnToMainScene()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        GameResult.Clear();
        NetworkManager.Singleton.SceneManager.LoadScene(mainSceneName, LoadSceneMode.Single);
    }
}