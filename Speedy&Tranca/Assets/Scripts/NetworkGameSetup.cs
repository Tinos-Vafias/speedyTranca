using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Lives INSIDE MainScene, as an in-scene NetworkObject next to GameManager.
public class NetworkGameSetup : NetworkBehaviour
{
    public Player playerOne;
    public Player playerTwo;
    public GameManager gameManager;

    private bool playerTwoAssigned;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        StartCoroutine(WaitForPlayersThenAssign());
    }

    // PlayerOne/PlayerTwo are separate in-scene NetworkObjects - there's no
    // guarantee they've finished spawning by the moment THIS object spawns,
    // so wait until they report IsSpawned before touching their ownership.
    // In practice this resolves within a frame or two.
    private IEnumerator WaitForPlayersThenAssign()
    {
        while (!playerOne.NetworkObject.IsSpawned || !playerTwo.NetworkObject.IsSpawned)
            yield return null;

        playerOne.NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

        // Handle anyone who connected before this finished, and anyone who
        // joins a little later.
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            TryAssignPlayerTwo(clientId);

        NetworkManager.Singleton.OnClientConnectedCallback += TryAssignPlayerTwo;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= TryAssignPlayerTwo;
    }

    private void TryAssignPlayerTwo(ulong clientId)
    {
        if (!IsServer || playerTwoAssigned) return;
        if (clientId == NetworkManager.ServerClientId) return; // that's player one

        playerTwo.NetworkObject.ChangeOwnership(clientId);
        playerTwoAssigned = true;
        Debug.Log($"Assigned Player Two to client {clientId}.");

        gameManager.BeginGame();
    }
}