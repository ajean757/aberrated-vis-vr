using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

public class UISelectionManager : MonoBehaviour
{
    public List<Button> buttons;
    public UnityEvent<string> OnSelect;
    public GameObject selected;
    //public GameObject lastSelected;

    public UISelectionManager above;
    public UISelectionManager below;

    public Selectable altBelow;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
				/*        for (int i = 0; i < Mathf.Min(selections.Count, transform.childCount); i++)
                {
                    buttons[i] = transform.GetComponentInChild
                }*/
				buttons = new();
				GetComponentsInChildren(buttons);
				for (int i = 0; i < buttons.Count; i++)
				{
            Navigation nav = buttons[i].navigation;

            nav.selectOnRight = i == buttons.Count - 1 ? null : buttons[i + 1];
            nav.selectOnLeft = i == 0 ? null : buttons[i - 1];

            buttons[i].navigation = nav;

/*            EventTrigger trigger = buttons[i].gameObject.GetComponent<EventTrigger>();
            EventTrigger.Entry entry = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            entry.callback.AddListener((eventData) => OnButtonSelected((BaseEventData)eventData));*/
				}
        //selected = buttons[0].gameObject;
    }

		private void Start()
		{
        OnButtonSelected(buttons[0].gameObject);

				if (altBelow != null)
				{
						foreach (Button button in buttons)
						{
								Navigation nav = button.navigation;
								nav.selectOnDown = altBelow;
								button.navigation = nav;
						}
				}
		}

    // Update is called once per frame
    void Update()
    {
        //Debug.Log()
    }

    public void OnButtonSelected(GameObject obj)
		{
        //int index = buttons.FindIndex(button => button.gameObject == eventData.selectedObject);
        //Debug.Log("selected " + );
        if (selected != null)
            selected.GetComponent<Outline>().enabled = false;
        selected = obj;
        selected.GetComponent<Outline>().enabled = true;
        OnSelect.Invoke(selected.GetComponent<ButtonManager>().selection);

        if (above != null)
				{
            foreach (Button button in above.buttons)
						{
                Navigation nav = button.navigation;
                nav.selectOnDown = obj.GetComponent<Button>();
                button.navigation = nav;
						}
				}

        if (below != null)
        {
            foreach (Button button in below.buttons)
            {
                Navigation nav = button.navigation;
                nav.selectOnUp = obj.GetComponent<Button>();
                button.navigation = nav;
            }
        }

        if (altBelow != null)
				{
            Navigation nav = altBelow.navigation;
            nav.selectOnUp = obj.GetComponent<Button>();
            altBelow.navigation = nav;
				}
    }
}
