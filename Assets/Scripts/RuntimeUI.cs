using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RhythmGame
{
    /// <summary>
    /// Shared runtime UI-building helpers (same procedural-at-runtime approach as LaneSetup),
    /// factored out of SongSelectMenu so StartMenu can build matching panels/buttons/sliders
    /// without duplicating the hierarchy-construction code. Every button built here plays the
    /// UI click sound centrally, so any caller gets it for free.
    /// </summary>
    public static class RuntimeUI
    {
        public static GameObject CreatePanel(Transform canvasParent, string name, Color backgroundColor)
        {
            var panelGO = new GameObject(name, typeof(RectTransform));
            panelGO.transform.SetParent(canvasParent, false);
            var rt = panelGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var image = panelGO.AddComponent<Image>();
            image.color = backgroundColor;
            return panelGO;
        }

        public static TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.color = Color.white;
            // Shrink long labels to fit on one line rather than wrapping and overflowing
            // the item's fixed height.
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = fontSize * 0.5f;
            tmp.fontSizeMax = fontSize;
            return tmp;
        }

        // A button-styled list item. Plays the UI click sound centrally so every caller gets
        // it for free instead of having to remember to wire it in themselves.
        public static GameObject CreateButton(Transform parent, string name, string label,
            Color itemColor, Color hoverColor, float height, System.Action onClick)
        {
            var itemGO = new GameObject(name, typeof(RectTransform));
            itemGO.transform.SetParent(parent, false);
            var le = itemGO.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            var image = itemGO.AddComponent<Image>();
            image.color = itemColor;

            var button = itemGO.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = itemColor;
            colors.highlightedColor = hoverColor;
            colors.pressedColor = hoverColor;
            button.colors = colors;
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                SoundSettings.Instance?.PlayClick();
                onClick?.Invoke();
            });

            TextMeshProUGUI labelText = CreateText(itemGO.transform, "Label", label, 24, TextAlignmentOptions.Left);
            RectTransform labelRT = labelText.rectTransform;
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(16f, 0f);
            labelRT.offsetMax = new Vector2(-16f, 0f);

            return itemGO;
        }

        public static GameObject CreateEmptyLabel(Transform parent, string message, float height)
        {
            TextMeshProUGUI empty = CreateText(parent, "EmptyLabel", message, 20, TextAlignmentOptions.Center);
            var le = empty.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return empty.gameObject;
        }

        // A scrollable, vertically-listed panel with a title and optional back button —
        // the shape used by every "pick one of these" step (songs, parts, hands, etc.).
        // onBack == null means no back button (used for root-level steps).
        public static (GameObject panel, RectTransform content) BuildListPanel(Transform canvasParent,
            string title, System.Action onBack, Color backgroundColor, Color itemColor, Color hoverColor,
            float itemHeight, float itemSpacing)
        {
            GameObject panel = CreatePanel(canvasParent, title.Replace(" ", "") + "Panel", backgroundColor);

            TextMeshProUGUI titleText = CreateText(panel.transform, "Title", title, 32, TextAlignmentOptions.Center);
            RectTransform titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -30f);
            titleRect.sizeDelta = new Vector2(0f, 60f);

            if (onBack != null)
                CreateBackButton(panel.transform, itemColor, hoverColor, onBack);

            var scrollGO = new GameObject("ScrollView", typeof(RectTransform));
            scrollGO.transform.SetParent(panel.transform, false);
            var scrollRT = scrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0.1f, 0.05f);
            scrollRT.anchorMax = new Vector2(0.9f, 0.85f);
            scrollRT.offsetMin = Vector2.zero;
            scrollRT.offsetMax = Vector2.zero;
            var scrollRect = scrollGO.AddComponent<ScrollRect>();

            var viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            var viewportRT = viewportGO.GetComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            var viewportImage = viewportGO.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
            viewportGO.AddComponent<RectMask2D>();

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            RectTransform content = contentGO.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            // A freshly added RectTransform defaults to sizeDelta (100, 100). Left unset, that
            // stray +100 width (on top of the 0..1 anchor stretch) made Content 100px wider than
            // the viewport, so every item spilled past the RectMask2D and got clipped.
            content.sizeDelta = Vector2.zero;

            var layout = contentGO.AddComponent<VerticalLayoutGroup>();
            layout.spacing = itemSpacing;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.padding = new RectOffset(4, 4, 4, 4);

            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRT;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 20f;

            panel.SetActive(false);
            return (panel, content);
        }

        public static GameObject CreateBackButton(Transform parent, Color itemColor, Color hoverColor, System.Action onBack)
        {
            GameObject backGO = CreateButton(parent, "BackButton", "< Back", itemColor, hoverColor, 40f, onBack);
            var backRT = backGO.GetComponent<RectTransform>();
            backRT.anchorMin = new Vector2(0f, 1f);
            backRT.anchorMax = new Vector2(0f, 1f);
            backRT.pivot = new Vector2(0f, 1f);
            backRT.anchoredPosition = new Vector2(20f, -20f);
            backRT.sizeDelta = new Vector2(110f, 40f);
            return backGO;
        }

        // A horizontal slider with the standard Background/Fill/Handle sub-hierarchy Unity's
        // UI system expects. Caller wires up onValueChanged/initial value themselves.
        public static Slider CreateSlider(Transform parent, string name,
            Color trackColor, Color fillColor, Color handleColor, float minValue, float maxValue, bool wholeNumbers)
        {
            var sliderGO = new GameObject(name, typeof(RectTransform));
            sliderGO.transform.SetParent(parent, false);

            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.wholeNumbers = wholeNumbers;
            slider.direction = Slider.Direction.LeftToRight;

            var bgGO = new GameObject("Background", typeof(RectTransform));
            bgGO.transform.SetParent(sliderGO.transform, false);
            var bgRT = bgGO.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.25f);
            bgRT.anchorMax = new Vector2(1f, 0.75f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            bgGO.AddComponent<Image>().color = trackColor;

            var fillAreaGO = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaGO.transform.SetParent(sliderGO.transform, false);
            var fillAreaRT = fillAreaGO.GetComponent<RectTransform>();
            fillAreaRT.anchorMin = new Vector2(0f, 0.25f);
            fillAreaRT.anchorMax = new Vector2(1f, 0.75f);
            fillAreaRT.offsetMin = new Vector2(5f, 0f);
            fillAreaRT.offsetMax = new Vector2(-5f, 0f);

            var fillGO = new GameObject("Fill", typeof(RectTransform));
            fillGO.transform.SetParent(fillAreaGO.transform, false);
            var fillRT = fillGO.GetComponent<RectTransform>();
            fillRT.anchorMin = new Vector2(0f, 0f);
            fillRT.anchorMax = new Vector2(0f, 1f);
            fillRT.sizeDelta = new Vector2(10f, 0f);
            fillGO.AddComponent<Image>().color = fillColor;
            slider.fillRect = fillRT;

            var handleAreaGO = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaGO.transform.SetParent(sliderGO.transform, false);
            var handleAreaRT = handleAreaGO.GetComponent<RectTransform>();
            handleAreaRT.anchorMin = Vector2.zero;
            handleAreaRT.anchorMax = Vector2.one;
            handleAreaRT.offsetMin = Vector2.zero;
            handleAreaRT.offsetMax = Vector2.zero;

            var handleGO = new GameObject("Handle", typeof(RectTransform));
            handleGO.transform.SetParent(handleAreaGO.transform, false);
            var handleRT = handleGO.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(24f, 0f);
            var handleImage = handleGO.AddComponent<Image>();
            handleImage.color = handleColor;
            slider.handleRect = handleRT;
            slider.targetGraphic = handleImage;

            return slider;
        }

        public static void ClearItems(List<GameObject> items)
        {
            foreach (GameObject go in items) Object.Destroy(go);
            items.Clear();
        }
    }
}
