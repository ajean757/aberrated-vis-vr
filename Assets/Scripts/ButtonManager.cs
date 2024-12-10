using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ButtonManager : MonoBehaviour, ISelectHandler
{
    public string selection;
    private UISelectionManager parent;

    void Awake()
    {
        parent = GetComponentInParent<UISelectionManager>();
        //parent.buttons.Add(GetComponent<Button>());
    }

    public void OnSelect(BaseEventData eventData)
		{
        parent.OnButtonSelected(gameObject);
		}
}
