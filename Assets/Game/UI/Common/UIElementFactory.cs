using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    internal static class UIElementFactory
    {
        public static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.11f, 0.94f);
        public static readonly Color BlockColor = new Color(0.14f, 0.16f, 0.19f, 0.96f);
        public static readonly Color ButtonColor = new Color(0.22f, 0.27f, 0.32f, 1f);
        public static readonly Color ButtonHighlightColor = new Color(0.30f, 0.37f, 0.44f, 1f);
        public static readonly Color TextColor = new Color(0.92f, 0.94f, 0.96f, 1f);
        public static readonly Color MutedTextColor = new Color(0.65f, 0.70f, 0.76f, 1f);
        public static readonly Color AccentColor = new Color(0.21f, 0.63f, 0.91f, 1f);

        public static void Stretch(RectTransform rectTransform)
        {
            if (rectTransform == null)
            {
                return;
            }

            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.localScale = Vector3.one;
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        public static Image CreateImage(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            string text,
            int fontSize,
            TextAlignmentOptions alignment,
            Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            return label;
        }

        public static Button CreateButton(string name, Transform parent, string text)
        {
            Image image = CreateImage(name, parent, ButtonColor);
            Button button = image.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = ButtonHighlightColor;
            colors.pressedColor = AccentColor;
            colors.selectedColor = ButtonHighlightColor;
            colors.disabledColor = new Color(0.12f, 0.13f, 0.15f, 1f);
            colors.colorMultiplier = 1f;
            button.colors = colors;

            TextMeshProUGUI label = CreateText("Label", image.transform, text, 24, TextAlignmentOptions.Center, TextColor);
            Stretch(label.rectTransform);
            return button;
        }

        public static Slider CreateSlider(string name, Transform parent)
        {
            RectTransform root = CreateRect(name, parent);
            root.sizeDelta = new Vector2(360f, 28f);

            Image background = CreateImage("Background", root, new Color(0.10f, 0.11f, 0.13f, 1f));
            Stretch(background.rectTransform);

            RectTransform fillArea = CreateRect("Fill Area", root);
            fillArea.anchorMin = new Vector2(0f, 0.25f);
            fillArea.anchorMax = new Vector2(1f, 0.75f);
            fillArea.offsetMin = new Vector2(6f, 0f);
            fillArea.offsetMax = new Vector2(-6f, 0f);

            Image fill = CreateImage("Fill", fillArea, AccentColor);
            Stretch(fill.rectTransform);

            RectTransform handleArea = CreateRect("Handle Slide Area", root);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);

            Image handle = CreateImage("Handle", handleArea, TextColor);
            RectTransform handleRect = handle.rectTransform;
            handleRect.anchorMin = new Vector2(0.5f, 0.5f);
            handleRect.anchorMax = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(20f, 34f);

            Slider slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            return slider;
        }

        public static Toggle CreateToggle(string name, Transform parent, string text)
        {
            RectTransform root = CreateRect(name, parent);
            root.sizeDelta = new Vector2(360f, 36f);

            Image box = CreateImage("Checkmark Box", root, new Color(0.10f, 0.11f, 0.13f, 1f));
            RectTransform boxRect = box.rectTransform;
            boxRect.anchorMin = new Vector2(0f, 0.5f);
            boxRect.anchorMax = new Vector2(0f, 0.5f);
            boxRect.anchoredPosition = new Vector2(18f, 0f);
            boxRect.sizeDelta = new Vector2(28f, 28f);

            Image checkmark = CreateImage("Checkmark", box.transform, AccentColor);
            RectTransform checkRect = checkmark.rectTransform;
            checkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkRect.sizeDelta = new Vector2(16f, 16f);

            TextMeshProUGUI label = CreateText("Label", root, text, 22, TextAlignmentOptions.Left, TextColor);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(52f, 0f);
            labelRect.offsetMax = Vector2.zero;

            Toggle toggle = root.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = box;
            toggle.graphic = checkmark;
            return toggle;
        }

        public static Image CreateBar(string name, Transform parent, string label, Color fillColor)
        {
            RectTransform root = CreateRect(name, parent);
            root.sizeDelta = new Vector2(320f, 28f);

            Image background = CreateImage("Background", root, new Color(0.05f, 0.06f, 0.07f, 0.90f));
            Stretch(background.rectTransform);

            Image fill = CreateImage("Fill", root, fillColor);
            RectTransform fillRect = fill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);

            TextMeshProUGUI text = CreateText("Label", root, label, 18, TextAlignmentOptions.Center, TextColor);
            Stretch(text.rectTransform);
            return fill;
        }
    }
}
