using BallSortGame.Core;
using BallSortGame.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.LevelEditor
{
    /// <summary>
    /// "PLAY AS BOT": the <see cref="LevelBot"/> plays the loaded pack from level 1 on the real board with the HUD
    /// showing, a bar at the bottom reports progress (speed toggle, STOP), and when it is through (or stopped) a report
    /// lists every issue; tapping an issue loads that fill into the editor so it can be fixed and saved.
    /// </summary>
    public partial class LevelEditorManager
    {
        static readonly float[] BotSpeeds = { 1f, 4f, 16f };

        LevelBot _bot;
        bool _botting;
        int _botSpeedIndex = 1;
        GameObject _botBar, _report;
        Text _botStatus, _botSpeedText, _reportTitle, _reportSummary, _packText;
        RectTransform _reportList;

        void RefreshPackLabel()
        {
            if (_packText == null) return;
            int n = game.Stage?.Levels.Count ?? 0;
            _packText.text = n > 0 ? $"PACK {game.levelPackResource}  -  {n} levels" : "No level pack loaded";
        }

        // ------------------------------------------------------------------ run

        void PlayAsBot()
        {
            if (game.Stage == null || game.Stage.Levels.Count == 0) { Status("No level pack is loaded in GameManager.", EditorUI.Bad); return; }
            if (_job != null) { Status("Still working on the last job...", EditorUI.Bad); return; }
            if (_bot == null)
            {
                _bot = gameObject.AddComponent<LevelBot>();
                _bot.game = game;
                _bot.Finished += OnBotFinished;
            }
            _botting = true;
            _testing = false;
            _panel.SetActive(false);
            _report.SetActive(false);
            _botBar.SetActive(true);
            game.MinCameraSize = 0f;
            ShowHud(true);
            var menu = FindFirstObjectByType<MainMenuView>();
            if (menu != null && MainMenuView.IsOpen) menu.Play();
            _bot.Speed = BotSpeeds[_botSpeedIndex];
            _bot.Run(1);
            UpdateBotBar();
        }

        void UpdateBot()
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F2))
            {
                if (_bot.IsRunning) _bot.Stop();
                else StopBot(true);
                return;
            }
            if (_bot.IsRunning) UpdateBotBar();
        }

        void UpdateBotBar()
        {
            if (_botStatus == null) return;
            int errors = _bot.ErrorCount;
            string issues = _bot.Issues.Count == 0 ? "no issues" : $"{errors} error(s), {_bot.Issues.Count - errors} warning(s)";
            _botStatus.text = $"{_bot.Status}\n{_bot.Won} won  -  {issues}";
            _botSpeedText.text = $"x{BotSpeeds[_botSpeedIndex]:0}";
        }

        void CycleBotSpeed()
        {
            _botSpeedIndex = (_botSpeedIndex + 1) % BotSpeeds.Length;
            if (_bot != null)
            {
                _bot.Speed = BotSpeeds[_botSpeedIndex];
                if (_bot.IsRunning) Time.timeScale = _bot.Speed;
            }
            UpdateBotBar();
        }

        /// <summary>The bot is through or was stopped: show the report over the board.</summary>
        void OnBotFinished()
        {
            if (!_botting) return;
            _botBar.SetActive(false);
            string report = _bot.Report();
            Debug.Log(report);
            ShowReport();
        }

        /// <summary>Leaves bot mode; <paramref name="backToEditor"/> reopens the editor panel, otherwise the caller decides.</summary>
        void StopBot(bool backToEditor)
        {
            if (_bot != null && _bot.IsRunning) _bot.Stop();
            _botting = false;
            _botBar.SetActive(false);
            _report.SetActive(false);
            Time.timeScale = 1f;
            game.IsBotPlaying = false;
            if (backToEditor)
            {
                ShowHud(false);
                _panel.SetActive(true);
                Refresh();
            }
        }

        // ------------------------------------------------------------------ report

        void ShowReport()
        {
            foreach (Transform child in _reportList) Destroy(child.gameObject);
            _reportTitle.text = _bot.Stopped ? "BOT REPORT (STOPPED)" : "BOT REPORT";
            _reportSummary.text = _bot.Summary();
            _reportSummary.color = _bot.ErrorCount > 0 ? EditorUI.Bad : EditorUI.Good;
            if (_bot.Issues.Count == 0)
                EditorUI.ListButton(_reportList, "No issues found. Every level is well-formed and the bot won them all.", EditorUI.Bar, null, 110f);
            foreach (var issue in _bot.Issues)
            {
                var captured = issue;
                var color = issue.Severity == IssueSeverity.Error ? new Color(0.55f, 0.22f, 0.22f, 1f) : new Color(0.45f, 0.38f, 0.16f, 1f);
                string id = string.IsNullOrEmpty(issue.PuzzleId) ? "" : $"  [{issue.PuzzleId}]";
                var b = EditorUI.ListButton(_reportList, $"{issue.Where}{id}\n{issue.Message}", color, issue.Data != null ? () => EditIssue(captured) : (UnityEngine.Events.UnityAction)null, 150f);
                var t = b.GetComponentInChildren<Text>();
                t.resizeTextMaxSize = 32;
                t.alignment = TextAnchor.MiddleLeft;
            }
            _report.SetActive(true);
        }

        /// <summary>Loads the fill of an issue into the editor so it can be fixed and saved.</summary>
        void EditIssue(LevelIssue issue)
        {
            StopBot(true);
            string name = string.IsNullOrEmpty(issue.PuzzleId) ? $"lv{issue.Level}_fix" : issue.PuzzleId;
            try
            {
                var lv = EditorLevel.FromLevelData(issue.Data, name);
                lv.Slot = SlotOf(issue.Level);
                Replace(lv);
                Status($"Loaded {issue.Where}: {issue.Message}", EditorUI.Bad);
            }
            catch (System.Exception e)
            {
                Status($"Cannot load {issue.Where} into the editor: {e.Message}", EditorUI.Bad);
            }
        }

        void CopyReport()
        {
            GUIUtility.systemCopyBuffer = _bot.Report();
            _reportSummary.text = "Report copied to the clipboard (also in the Console).";
            _reportSummary.color = Color.white;
        }

        // ------------------------------------------------------------------ UI

        /// <summary>Row above the actions: pack label and the PLAY AS BOT button.</summary>
        void BuildPackRow(Transform panel)
        {
            var row = EditorUI.Row("pack", panel, false, 426f, 100f, 16f);
            var pill = EditorUI.Image("pack_label", row, EditorUI.PillSprite, EditorUI.Tint(EditorUI.Skin?.pill, EditorUI.Bar), true);
            EditorUI.Flex(pill, 1f);
            _packText = EditorUI.Shadowed(EditorUI.Label(pill.transform, "", 28, TextAnchor.MiddleCenter, Color.white, FontStyle.Normal));
            EditorUI.PillButton(row, "PLAY AS BOT", true, PlayAsBot, 330f, 40);
            EditorUI.PillButton(row, "SHARE", false, Share, 200f, 40);
        }

        void BuildBotUI(RectTransform root)
        {
            // ---- bot bar while playing: status pill, speed, STOP (above the booster bar, like the "< EDITOR" pill)
            var bar = EditorUI.Row("bot_bar", root, false, 320f, 110f, 16f);
            var pill = EditorUI.Image("bot_status", bar, EditorUI.PillSprite, EditorUI.Tint(EditorUI.Skin?.pill, EditorUI.Bar), true);
            EditorUI.Flex(pill, 1f);
            _botStatus = EditorUI.Shadowed(EditorUI.Label(pill.transform, "", 28, TextAnchor.MiddleCenter, Color.white, FontStyle.Normal));
            var speed = EditorUI.PillButton(bar, "x4", false, CycleBotSpeed, 150f, 40);
            _botSpeedText = speed.GetComponentInChildren<Text>();
            EditorUI.PillButton(bar, "STOP", false, () => { if (_bot.IsRunning) _bot.Stop(); else StopBot(true); }, 200f, 40);
            _botBar = bar.gameObject;
            _botBar.SetActive(false);

            // ---- report: dark page over the board with the issue list
            var page = EditorUI.Panel("bot_report", root, EditorUI.Background);
            page.raycastTarget = true;
            var top = EditorUI.Row("top_bar", page.transform, true, 68f, 146f, 24f);
            EditorUI.IconButton(top, "btn - close", EditorUI.Skin?.iconClose, "X", () => StopBot(true), 146f);
            var title = EditorUI.Image("title", top, EditorUI.PillSprite, EditorUI.Tint(EditorUI.Skin?.pill, EditorUI.Bar), true);
            EditorUI.Flex(title, 1f);
            _reportTitle = EditorUI.Shadowed(EditorUI.Label(title.transform, "BOT REPORT", 40, TextAnchor.UpperCenter, Color.white));
            _reportTitle.rectTransform.offsetMin = new Vector2(20f, 80f);
            _reportTitle.rectTransform.offsetMax = new Vector2(-20f, -14f);
            _reportSummary = EditorUI.Shadowed(EditorUI.Label(title.transform, "", 26, TextAnchor.UpperCenter, Color.white, FontStyle.Normal));
            _reportSummary.rectTransform.offsetMin = new Vector2(20f, 12f);
            _reportSummary.rectTransform.offsetMax = new Vector2(-20f, -66f);
            EditorUI.PillButton(top, "COPY", false, CopyReport, 200f, 40);
            _reportList = EditorUI.ScrollList(page.transform, new Vector2(24f, 150f), new Vector2(-24f, -240f));
            var hint = EditorUI.Band("hint", page.transform, false, 60f, 70f);
            EditorUI.Shadowed(EditorUI.Label(hint, "Tap an issue to load that level into the editor.", 28, TextAnchor.MiddleCenter, EditorUI.Muted, FontStyle.Normal));
            _report = page.gameObject;
            _report.SetActive(false);
        }
    }
}
