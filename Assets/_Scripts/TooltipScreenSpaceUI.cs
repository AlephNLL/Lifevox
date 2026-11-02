using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class TooltipScreenSpaceUI : MonoBehaviour
{
    public static TooltipScreenSpaceUI instance;
    [SerializeField] RectTransform canvasRect;
    private TextMeshProUGUI textMeshPro;
    private RectTransform rectTransform;

    private Vector2 textSize;
    private void Awake()
    {
        instance = this;
        textMeshPro = transform.Find("Text").GetComponent<TextMeshProUGUI>();
        rectTransform = GetComponent<RectTransform>();
        textSize = textMeshPro.GetRenderedValues(false);
    }

    private void SetText(string tooltipText)
    {
        textMeshPro.SetText(tooltipText);
        textMeshPro.ForceMeshUpdate();
        textSize = textMeshPro.GetRenderedValues(false);
    }

    private void Update()
    {
        Vector2 anchoredPosition = Input.mousePosition / canvasRect.localScale.x;

        if(anchoredPosition.x + textSize.x > canvasRect.rect.width)
        {
            anchoredPosition.x = canvasRect.rect.width - textSize.x;
        }
        if (anchoredPosition.y + textSize.y > canvasRect.rect.height)
        {
            anchoredPosition.y = canvasRect.rect.height - textSize.y;
        }
        rectTransform.anchoredPosition = anchoredPosition;
    }

    public static void SetText_Static(string tooltipText)
    {
        instance.SetText(tooltipText);
    }
}
