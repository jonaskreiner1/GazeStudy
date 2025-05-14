using UnityEngine;
using System.Collections;

public class CollisionUIButton : MonoBehaviour
{
    public Material defaultMaterial;    // Dark Blue

    public Material highlightMaterial; // Lighter Blue

    public GameObject SelectionButton;

    public static CollisionUIButton currentlyHighlighted = null; // Tracks the currently highlighted button

    private TrackingHandler trackingHandler; // Reference to main script

    // Dwell Time Variables
    private float triggerStartTime = 0f;
    private float triggerThreshold = 0.3f;


    private void Start()
    {
        GetComponent<Renderer>().material = defaultMaterial; // Ensure every button starts with its default material
        trackingHandler = FindObjectOfType<TrackingHandler>();
        if (trackingHandler == null) Debug.LogError("TrackingHandler not found in scene!");
    }

    private void OnTriggerEnter(Collider other)
    {
        trackingHandler.StartTimer(true, "ToT"); // Mark Time on Target

        // Handle Behaviour for each Selection Mode
        if (trackingHandler.SelectionMode >= 3 && trackingHandler.SelectionMode <= 5) trackingHandler.smoothing = 4; // Smooth Wink, Blink and Nodding
        else if (trackingHandler.SelectionMode == 1 || trackingHandler.SelectionMode == 6) SelectionButton.SetActive(true); // Activate SelectionButton
        else if (trackingHandler.SelectionMode == 2) triggerStartTime = Time.time; // Start Dwell Timer

        // Handle Button Highlighting
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null && highlightMaterial != null)
        {
            // Reset the previous button if there is one
            if (currentlyHighlighted != null && currentlyHighlighted != this)
            {
                currentlyHighlighted.ResetMaterial();
                currentlyHighlighted = null;
            }

            // Highlight this button
            renderer.material = highlightMaterial;
            currentlyHighlighted = this;
        }
    }

    private void OnTriggerStay(Collider other)
    {
        // Select Button when DwellTime is reached
        if (trackingHandler.SelectionMode == 2 && Time.time - triggerStartTime > triggerThreshold) ButtonSelected();
    }


    private void ResetMaterial()
    {
        // Reset material to default - Can be called remotely for other highlighted buttons
        GetComponent<Renderer>().material = defaultMaterial;

    }

    public void ButtonSelected()
    {
        if (currentlyHighlighted != null)
        {
            currentlyHighlighted.ResetMaterial(); // Reset material to default
            trackingHandler.StartTimer(false, currentlyHighlighted.gameObject.name); // Mark Selection
            trackingHandler.ToggleCanvas(false); // Turn off Canvas
            trackingHandler.smoothing = 10; // Return to regular smoothing Value
            currentlyHighlighted = null; // Reset the highlighted button
        }
        else Debug.LogError("ButtonSelection without Button being Highlighted!");
    }

    public string GetHighlightedButtonName()
    {
        return currentlyHighlighted ? currentlyHighlighted.name : "None";
    }

}

