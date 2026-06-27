using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;

namespace PokeBattle
{
    /// <summary>
    /// Builds the entire battle UI from code (Canvas, HP bars, speech bubble,
    /// move menu, scrolling log) so no manual scene wiring is required.
    /// BattleManager talks to it through the public methods below.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class BattleUI : MonoBehaviour
    {
        private Font _font;

        // Enemy widgets (top-right)
        private Text _enemyName;
        private RectTransform _enemyHpFill;
        private Text _enemyHpText;

        // Player widgets (bottom-left)
        private Text _playerName;
        private RectTransform _playerHpFill;
        private Text _playerHpText;

        // Speech + log
        private Text _speechText;
        private RectTransform _speechBubble;
        private Text _logText;
        private RectTransform _logContent;
        private ScrollRect _logScroll;

        // Move menu
        private RectTransform _moveMenu;
        private readonly List<Button> _moveButtons = new List<Button>();
        private readonly List<Text> _moveButtonLabels = new List<Text>();

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            EnsureEventSystem();
            BuildCanvas();
            BuildEnemyPanel();
            BuildPlayerPanel();
            BuildSpeechBubble();
            BuildLog();
            BuildMoveMenu();
            HideSpeech();
            HideMoveMenu();
        }

        // ---------- Public API used by BattleManager ----------

        public void SetEnemyCreature(Creature c)
        {
            _enemyName.text = $"{c.name}  ({c.type})";
            UpdateEnemyHP(c);
        }

        public void SetPlayerCreature(Creature c)
        {
            _playerName.text = $"{c.name}  ({c.type})";
            UpdatePlayerHP(c);
        }

        public void UpdateEnemyHP(Creature c)
        {
            _enemyHpFill.localScale = new Vector3(Mathf.Clamp01(c.HealthRatio), 1f, 1f);
            _enemyHpFill.GetComponent<Image>().color = HealthColor(c.HealthRatio);
            _enemyHpText.text = $"{c.currentHP}/{c.maxHP}";
        }

        public void UpdatePlayerHP(Creature c)
        {
            _playerHpFill.localScale = new Vector3(Mathf.Clamp01(c.HealthRatio), 1f, 1f);
            _playerHpFill.GetComponent<Image>().color = HealthColor(c.HealthRatio);
            _playerHpText.text = $"{c.currentHP}/{c.maxHP}";
        }

        public void AppendLog(string line)
        {
            if (string.IsNullOrEmpty(_logText.text))
                _logText.text = line;
            else
                _logText.text += "\n" + line;

            Canvas.ForceUpdateCanvases();
            if (_logScroll != null) _logScroll.verticalNormalizedPosition = 0f;
        }

        /// <summary>Shows a speech bubble. speakerIsPlayer puts it on the left.</summary>
        public void ShowSpeech(string speaker, string line, bool speakerIsPlayer)
        {
            _speechBubble.gameObject.SetActive(true);
            _speechText.text = $"{speaker}: \"{line}\"";

            // Nudge the bubble toward whoever is talking.
            _speechBubble.anchoredPosition = new Vector2(speakerIsPlayer ? -120f : 120f, _speechBubble.anchoredPosition.y);
        }

        public void HideSpeech()
        {
            _speechBubble.gameObject.SetActive(false);
        }

        /// <summary>Shows the move buttons for the given moves; onPick gets the move index.</summary>
        public void ShowMoveMenu(IList<Move> moves, Action<int> onPick)
        {
            _moveMenu.gameObject.SetActive(true);
            for (int i = 0; i < _moveButtons.Count; i++)
            {
                int index = i;
                bool active = i < moves.Count;
                _moveButtons[i].gameObject.SetActive(active);
                if (!active) continue;

                Move m = moves[i];
                _moveButtonLabels[i].text = $"{m.name}\n<{m.type} | Pow {m.power}>";
                _moveButtons[i].onClick.RemoveAllListeners();
                _moveButtons[i].onClick.AddListener(() => onPick?.Invoke(index));
            }
        }

        public void HideMoveMenu()
        {
            _moveMenu.gameObject.SetActive(false);
        }

        // ---------- UI construction ----------

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;

            var es = new GameObject("EventSystem", typeof(EventSystem));
            // Project uses the new Input System, so use its UI module.
            es.AddComponent<InputSystemUIInputModule>();
        }

        private void BuildCanvas()
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            // Background
            var bg = CreatePanel("Background", transform, new Color(0.10f, 0.12f, 0.18f, 1f));
            Stretch(bg, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            bg.SetAsFirstSibling();
        }

        private void BuildEnemyPanel()
        {
            RectTransform panel = CreatePanel("EnemyPanel", transform, new Color(0f, 0f, 0f, 0.35f));
            Anchor(panel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(400f, 90f));

            _enemyName = CreateText("EnemyName", panel, "Enemy", 26, TextAnchor.MiddleLeft);
            Anchor(_enemyName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -10f), new Vector2(360f, 32f));
            _enemyName.alignment = TextAnchor.UpperLeft;

            BuildHpBar(panel, out _enemyHpFill, out _enemyHpText, -50f);
        }

        private void BuildPlayerPanel()
        {
            RectTransform panel = CreatePanel("PlayerPanel", transform, new Color(0f, 0f, 0f, 0.35f));
            Anchor(panel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 200f), new Vector2(400f, 90f));

            _playerName = CreateText("PlayerName", panel, "Player", 26, TextAnchor.MiddleLeft);
            Anchor(_playerName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -10f), new Vector2(360f, 32f));
            _playerName.alignment = TextAnchor.UpperLeft;

            BuildHpBar(panel, out _playerHpFill, out _playerHpText, -50f);
        }

        private void BuildHpBar(RectTransform parent, out RectTransform fill, out Text hpText, float yOffset)
        {
            RectTransform back = CreatePanel("HPBack", parent, new Color(0.2f, 0.2f, 0.2f, 1f));
            Anchor(back, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, yOffset), new Vector2(260f, 22f));

            fill = CreatePanel("HPFill", back, new Color(0.2f, 0.85f, 0.2f, 1f));
            Stretch(fill, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fill.pivot = new Vector2(0f, 0.5f);
            // Re-anchor fill to left edge so localScale.x shrinks it from the right.
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(1f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            fill.localScale = Vector3.one;

            hpText = CreateText("HPText", parent, "0/0", 16, TextAnchor.MiddleRight);
            Anchor(hpText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(290f, yOffset), new Vector2(90f, 22f));
        }

        private void BuildSpeechBubble()
        {
            _speechBubble = CreatePanel("SpeechBubble", transform, new Color(1f, 1f, 1f, 0.95f));
            Anchor(_speechBubble, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(620f, 80f));

            _speechText = CreateText("SpeechText", _speechBubble, "", 24, TextAnchor.MiddleCenter);
            Stretch(_speechText.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 8f), new Vector2(-16f, -8f));
            _speechText.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            _speechText.fontStyle = FontStyle.Bold;
        }

        private void BuildLog()
        {
            RectTransform panel = CreatePanel("LogPanel", transform, new Color(0f, 0f, 0f, 0.45f));
            Anchor(panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(560f, 160f));

            var scrollGO = new GameObject("LogScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(Mask));
            var scrollRT = (RectTransform)scrollGO.transform;
            scrollRT.SetParent(panel, false);
            Stretch(scrollRT, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -10f));
            scrollGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            scrollGO.GetComponent<Mask>().showMaskGraphic = false;

            _logScroll = scrollGO.GetComponent<ScrollRect>();
            _logScroll.horizontal = false;
            _logScroll.vertical = true;
            _logScroll.movementType = ScrollRect.MovementType.Clamped;
            _logScroll.scrollSensitivity = 20f;

            // The Text itself is the scroll content; ContentSizeFitter grows it
            // vertically as lines are appended (Text provides a preferred height).
            _logText = CreateText("LogText", scrollRT, "", 18, TextAnchor.UpperLeft);
            _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _logText.verticalOverflow = VerticalWrapMode.Overflow;
            _logContent = _logText.rectTransform;
            _logContent.anchorMin = new Vector2(0f, 1f);
            _logContent.anchorMax = new Vector2(1f, 1f);
            _logContent.pivot = new Vector2(0.5f, 1f);
            _logContent.anchoredPosition = Vector2.zero;

            var fitter = _logContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _logScroll.content = _logContent;
        }

        private void BuildMoveMenu()
        {
            _moveMenu = CreatePanel("MoveMenu", transform, new Color(0f, 0f, 0f, 0.001f));
            Anchor(_moveMenu, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 20f), new Vector2(420f, 160f));

            // 2x2 grid of buttons.
            float bw = 200f, bh = 70f, gap = 10f;
            Vector2[] positions =
            {
                new Vector2(0f, bh + gap),
                new Vector2(bw + gap, bh + gap),
                new Vector2(0f, 0f),
                new Vector2(bw + gap, 0f)
            };

            for (int i = 0; i < 4; i++)
            {
                var btnGO = new GameObject($"Move{i}", typeof(RectTransform), typeof(Image), typeof(Button));
                var rt = (RectTransform)btnGO.transform;
                rt.SetParent(_moveMenu, false);
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = positions[i];
                rt.sizeDelta = new Vector2(bw, bh);

                var img = btnGO.GetComponent<Image>();
                img.color = new Color(0.85f, 0.85f, 0.9f, 1f);

                var btn = btnGO.GetComponent<Button>();
                var colors = btn.colors;
                colors.highlightedColor = new Color(1f, 1f, 0.7f, 1f);
                colors.pressedColor = new Color(0.7f, 0.7f, 0.9f, 1f);
                btn.colors = colors;

                var label = CreateText($"Move{i}Label", rt, "-", 18, TextAnchor.MiddleCenter);
                Stretch(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                label.color = new Color(0.1f, 0.1f, 0.15f, 1f);

                _moveButtons.Add(btn);
                _moveButtonLabels.Add(label);
            }
        }

        // ---------- Small UI helpers ----------

        private static Color HealthColor(float ratio)
        {
            if (ratio > 0.5f) return new Color(0.2f, 0.85f, 0.2f, 1f);
            if (ratio > 0.2f) return new Color(0.95f, 0.8f, 0.1f, 1f);
            return new Color(0.9f, 0.2f, 0.2f, 1f);
        }

        private RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return rt;
        }

        private Text CreateText(string name, Transform parent, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = min; // pivot follows anchor corner for predictable placement
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
        }

        private static void Stretch(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }
    }
}
