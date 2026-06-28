using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UIElements;
using UnityEngine.XR;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{
    // Attributes regarding the player
    // For game conditions
    private int score = 0;
    private bool hasWon = false;
    
    // For cards
    public GameObject cardPrefab;
    private List<Card> hand = new List<Card>();
    private int numCards;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        numCards = hand.Count;
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            generateHand();
            for (int i = 0; i < numCards; i++)
            {
                Debug.Log(hand[i]);
            }
        }
    }

    private void generateHand()
    {
        for (int i = 0; i < 11; i++)
        {
            // Preferred — instantiate a Card prefab
            GameObject cardObject = Instantiate(cardPrefab);
            Card card = cardObject.GetComponent<Card>();
            hand.Add(card);
        }
        numCards = hand.Count;
    }
}
