using UnityEngine;

public class CollisionSelectionButton : MonoBehaviour
{
    private void Start()
    {

    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.name != "Cursor") return; // Only allow collisions from the Cursor object

        if (CollisionUIButton.currentlyHighlighted != null)
        {
            CollisionUIButton.currentlyHighlighted.ButtonSelected();
            gameObject.SetActive(false);
        }
        else
        {
            Debug.LogError("There is no Currently Highlighted Button that could be selected with SelectionButton!");
        }
    }

    public void MakeInactive()
    {
        gameObject.SetActive(false);
    }
}


