// Plain static class (not a MonoBehaviour) used to pass the outcome of a
// finished game across a scene load. Static fields survive SceneManager.LoadScene
// (they're only cleared by a domain reload / app restart), so this needs no
// DontDestroyOnLoad object or singleton boilerplate - GameManager writes to it
// right before loading the winner scene, WinnerSceneController reads it on Start().
public static class GameResult
{
    public static bool HasResult { get; private set; }
    public static bool IsTie { get; private set; }
    public static string WinnerName { get; private set; }
    public static int WinnerScore { get; private set; }
    public static int OtherScore { get; private set; }

    public static void SetWinner(string winnerName, int winnerScore, int otherScore)
    {
        HasResult = true;
        IsTie = false;
        WinnerName = winnerName;
        WinnerScore = winnerScore;
        OtherScore = otherScore;
    }

    public static void SetTie(int score)
    {
        HasResult = true;
        IsTie = true;
        WinnerName = null;
        WinnerScore = score;
        OtherScore = score;
    }

    // Called when heading back to the main scene so a stale result can't
    // leak into the next game if the winner scene is ever revisited oddly.
    public static void Clear()
    {
        HasResult = false;
        IsTie = false;
        WinnerName = null;
        WinnerScore = 0;
        OtherScore = 0;
    }
}