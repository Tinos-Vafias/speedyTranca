using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

// Lives in MainMenu, on the same GameObject as NetworkManager. Handles both
// creating a Relay allocation (Host) and joining one via a code (Client), so
// neither of you needs to deal with IP addresses or port forwarding.
public class NetworkDebugUI : MonoBehaviour
{
    public string gameplaySceneName = "MainScene";
    public int maxConnections = 1; // just the one other player joining the host

    private bool servicesReady;
    private bool actionInProgress;
    private bool hasLeftMenu;
    private string statusMessage = "";
    private string joinCodeInput = "";
    private string hostJoinCode = "";

    async void Start()
    {
        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            servicesReady = true;
        }
        catch (Exception e)
        {
            // Most common cause: this Unity project isn't linked to a Unity
            // Cloud project yet (Edit -> Project Settings -> Services).
            statusMessage = $"Couldn't reach online services: {e.Message}";
            Debug.LogError(statusMessage);
        }
    }

    void OnGUI()
    {
        if (hasLeftMenu) return; // no longer on this screen - stop drawing over the board

        GUI.Box(new Rect(10, 10, 320, 190), "Play Online");

        if (!servicesReady)
        {
            GUI.Label(new Rect(20, 40, 300, 60),
                string.IsNullOrEmpty(statusMessage) ? "Connecting to online services..." : statusMessage);
            return;
        }

        if (actionInProgress)
        {
            GUI.Label(new Rect(20, 40, 300, 40), statusMessage);
            return;
        }

        if (!string.IsNullOrEmpty(hostJoinCode))
        {
            GUI.Label(new Rect(20, 40, 300, 20), "Share this code with your friend:");
            GUI.Label(new Rect(20, 65, 300, 35), hostJoinCode,
                new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold });

            if (GUI.Button(new Rect(20, 110, 280, 25), "Copy Code"))
                GUIUtility.systemCopyBuffer = hostJoinCode;

            if (GUI.Button(new Rect(20, 145, 280, 30), "Continue"))
                ContinueToGame();

            return;
        }

        if (GUI.Button(new Rect(20, 40, 280, 30), "Host Game"))
            _ = StartHostAsync();

        GUI.Label(new Rect(20, 85, 280, 20), "Or enter a friend's code to join:");
        joinCodeInput = GUI.TextField(new Rect(20, 110, 180, 25), joinCodeInput);

        if (GUI.Button(new Rect(210, 110, 90, 25), "Join"))
            _ = JoinGameAsync(joinCodeInput.Trim());

        if (!string.IsNullOrEmpty(statusMessage))
            GUI.Label(new Rect(20, 145, 300, 40), statusMessage);
    }

    private async Task StartHostAsync()
    {
        actionInProgress = true;
        statusMessage = "Creating relay allocation...";

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            NetworkManager.Singleton.GetComponent<UnityTransport>()
                .SetRelayServerData(new RelayServerData(allocation, "dtls"));

            bool started = NetworkManager.Singleton.StartHost();
            if (!started)
            {
                statusMessage = "StartHost() failed - check the Console for details.";
                actionInProgress = false;
                return;
            }

            hostJoinCode = joinCode;
            statusMessage = "";
            // No LoadScene here anymore - the host clicks Continue (below,
            // in OnGUI) once they've actually shared the code.
        }
        catch (Exception e)
        {
            statusMessage = $"Couldn't host: {e.Message}";
            Debug.LogError(statusMessage);
        }
        finally
        {
            actionInProgress = false;
        }
    }

    private void ContinueToGame()
    {
        NetworkManager.Singleton.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
        hasLeftMenu = true;
    }

    private async Task JoinGameAsync(string joinCode)
    {
        if (string.IsNullOrEmpty(joinCode))
        {
            statusMessage = "Enter a join code first.";
            return;
        }

        actionInProgress = true;
        statusMessage = "Joining...";

        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            NetworkManager.Singleton.GetComponent<UnityTransport>()
                .SetRelayServerData(new RelayServerData(joinAllocation, "dtls"));

            bool started = NetworkManager.Singleton.StartClient();
            if (started)
            {
                hasLeftMenu = true;
                statusMessage = "";
            }
            else
            {
                statusMessage = "StartClient() failed - check the Console for details.";
            }
        }
        catch (Exception e)
        {
            statusMessage = $"Couldn't join: {e.Message}";
            Debug.LogError(statusMessage);
        }
        finally
        {
            actionInProgress = false;
        }
    }
}