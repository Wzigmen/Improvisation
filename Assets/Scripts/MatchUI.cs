using System;
using System.Collections;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Everything the player sees around a fight, built in code like the other menus:
//  - the "Play" button when the host stands on the color zone
//  - the fade to black + "Loading..." while everybody is moved to the ring
//  - the "Ready?" menu, the 3-2-1 countdown, the health bar, and the winner screen
public class MatchUI : MonoBehaviour
{
    public static MatchUI Instance { get; private set; }

    // "Play" prompt (bottom of the screen)
    RectTransform playPrompt;
    Text promptTitle;
    // ready menu
    RectTransform readyPanel;
    Text readyTitle;
    Text readyPlayers;
    Button readyButton;
    // the host's "how many bots" row (only in Bots mode)
    RectTransform botRow;
    Text botLabel;
    Button botMinus;
    Button botPlus;
    // countdown / "FIGHT!"
    Text bigText;
    // fight HUD
    RectTransform hud;
    RectTransform hpFill;
    Text hpText;
    Text infoText;
    Text knockedOutText;
    // winner screen
    RectTransform resultPanel;
    Text resultTitle;
    Text resultSub;
    // loading fade
    Image fade;
    Text fadeText;

    ColorZone[] zones = new ColorZone[0];
    ColorZone activeZone;   // the zone the local player is standing in, if any
    MatchMode promptMode;   // what pressing Play will start
    PlayerController local;
    float nextLookup;
    float nextListUpdate;
    bool fading;
    bool cursorFreed;
    MatchManager.Phase lastPhase = MatchManager.Phase.Lobby;
    float fightStartedAt = -10f;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        PauseMenu.UiWantsCursor = false;
    }

    void Start()
    {
        UIKit.EnsureEventSystem();
        BuildUI();
    }

    // ---- loading transition -------------------------------------------------------------------

    // Fades to black, runs `atBlack` (the teleport), shows "Loading..." for a moment, then fades back in.
    public void Transition(Action atBlack)
    {
        if (fading) { atBlack?.Invoke(); return; }
        StartCoroutine(TransitionRoutine(atBlack));
    }

    IEnumerator TransitionRoutine(Action atBlack)
    {
        fading = true;
        fadeText.text = "Загрузка…";

        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            SetFade(t / 0.35f);
            yield return null;
        }
        SetFade(1f);

        atBlack?.Invoke();
        yield return new WaitForSecondsRealtime(0.9f);

        for (float t = 0f; t < 0.45f; t += Time.unscaledDeltaTime)
        {
            SetFade(1f - t / 0.45f);
            yield return null;
        }
        SetFade(0f);
        fading = false;
    }

    void SetFade(float alpha)
    {
        fade.color = new Color(0f, 0f, 0f, alpha);
        fadeText.color = new Color(1f, 1f, 1f, alpha);
    }

    // ---- per-frame state ----------------------------------------------------------------------

    void Update()
    {
        if (Time.unscaledTime >= nextLookup)
        {
            nextLookup = Time.unscaledTime + 0.5f;
            zones = FindObjectsByType<ColorZone>();
            local = null;
            foreach (var p in FindObjectsByType<PlayerController>())
            {
                // Bots are owned by the host too, but they are not "us".
                if (p.IsSpawned && p.IsOwner && !p.IsBot) { local = p; break; }
            }
        }

        activeZone = null;
        foreach (var z in zones)
        {
            if (z != null && z.LocalPlayerInside) { activeZone = z; break; }
        }

        var match = MatchManager.Instance;
        bool active = NetworkGame.InGame && match != null && local != null && local.IsSpawned;
        var phase = active ? match.CurrentPhase : MatchManager.Phase.Lobby;
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        bool fighter = active && local.InMatch;

        if (phase != lastPhase)
        {
            if (phase == MatchManager.Phase.Fight) fightStartedAt = Time.unscaledTime;
            lastPhase = phase;
        }

        // The host (or the solo player, who is the host) gets a Play button while standing on the pad.
        bool showPlay = active && phase == MatchManager.Phase.Lobby && isHost && activeZone != null &&
                        !PauseMenu.IsPaused && !fading;
        SetShown(playPrompt, showPlay);
        if (showPlay)
        {
            promptMode = activeZone.Mode;
            promptTitle.text = promptMode == MatchMode.Bots ? "Бой с ботами: всё готово?" : "Всё готово к бою?";
            if (PlayPressed()) OnPlayClicked();
        }

        // "Ready?" menu; the cursor is freed while it's open.
        bool showReady = fighter && phase == MatchManager.Phase.Ready && !fading && !PauseMenu.IsPaused;
        SetShown(readyPanel, showReady);
        if (showReady)
        {
            bool botsMode = match.Mode == MatchMode.Bots;
            readyTitle.text = botsMode ? "БОЙ С БОТАМИ" : "БОЙ";

            // The host chooses how many bots to fight; everybody else just sees them in the list.
            SetShown(botRow, botsMode && isHost);
            if (botsMode && isHost)
            {
                botLabel.text = $"Ботов: {match.BotCount}";
                botMinus.interactable = match.BotCount > 1;
                botPlus.interactable = match.BotCount < MatchManager.MaxBots;
            }

            if (Time.unscaledTime >= nextListUpdate)
            {
                nextListUpdate = Time.unscaledTime + 0.25f;
                RefreshReadyList();
            }
            UIKit.SetButtonText(readyButton, local.IsReady ? "Не готов" : "Готов");
            if (EnterPressed()) OnReadyClicked();
        }
        UpdateCursor(showReady);

        UpdateCountdown(match, phase, fighter);
        UpdateHud(match, phase, fighter);
        UpdateResult(match, phase, fighter);
    }

    void UpdateCursor(bool wantsCursor)
    {
        if (wantsCursor == cursorFreed) return;
        cursorFreed = wantsCursor;
        PauseMenu.UiWantsCursor = wantsCursor;

        if (wantsCursor) PauseMenu.SetCursorCaptured(false);
        else if (NetworkGame.InGame && !PauseMenu.IsPaused && !PauseMenu.PanelOpen) PauseMenu.SetCursorCaptured(true);
    }

    void UpdateCountdown(MatchManager match, MatchManager.Phase phase, bool fighter)
    {
        string text = null;
        float scale = 1f;

        if (fighter && phase == MatchManager.Phase.Countdown)
        {
            float left = match.SecondsLeft;
            int n = Mathf.Max(1, Mathf.CeilToInt(left));
            text = n.ToString();
            scale = 1f + (left - Mathf.Floor(left)) * 0.45f; // each number swells and shrinks
        }
        else if (fighter && phase == MatchManager.Phase.Fight && Time.unscaledTime - fightStartedAt < 1f)
        {
            text = "БОЙ!";
            scale = 1f + (1f - (Time.unscaledTime - fightStartedAt)) * 0.3f;
        }

        SetShown(bigText.rectTransform, text != null);
        if (text != null)
        {
            bigText.text = text;
            bigText.rectTransform.localScale = Vector3.one * scale;
        }
    }

    void UpdateHud(MatchManager match, MatchManager.Phase phase, bool fighter)
    {
        bool show = fighter && phase != MatchManager.Phase.Ready;
        SetShown(hud, show);
        SetShown(knockedOutText.rectTransform, fighter && local.IsDead && phase == MatchManager.Phase.Fight);
        if (!show) return;

        float ratio = Mathf.Clamp01(local.Health / (float)PlayerController.MaxHealth);
        hpFill.anchorMax = new Vector2(ratio, 1f);
        hpText.text = $"{local.Health} / {PlayerController.MaxHealth}";

        if (match.StartedWith >= 2)
        {
            int alive = 0;
            foreach (var p in MatchManager.ActivePlayers(true))
            {
                if (p.Health > 0) alive++;
            }
            infoText.text = $"Живых: {alive} из {match.StartedWith}";
        }
        else
        {
            infoText.text = "Тренировка. Esc — завершить бой";
        }
    }

    void UpdateResult(MatchManager match, MatchManager.Phase phase, bool fighter)
    {
        bool show = fighter && phase == MatchManager.Phase.Result;
        SetShown(resultPanel, show);
        if (!show) return;

        long winner = match.WinnerObjectId;
        PlayerController winnerPlayer = null;
        if (winner >= 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue((ulong)winner, out var winnerObject))
            winnerPlayer = winnerObject.GetComponent<PlayerController>();

        if (winner < 0) resultTitle.text = "Ничья";
        else if (winnerPlayer == local) resultTitle.text = "Вы победили!";
        else if (winnerPlayer != null) resultTitle.text = $"Победил {winnerPlayer.DisplayName}";
        else resultTitle.text = "Бой окончен";
        resultSub.text = $"Возвращение на поле через {Mathf.CeilToInt(match.SecondsLeft)}";
    }

    // ---- buttons ------------------------------------------------------------------------------

    void OnPlayClicked()
    {
        if (MatchManager.Instance != null) MatchManager.Instance.StartMatchRpc((byte)promptMode);
    }

    void ChangeBots(int delta)
    {
        var match = MatchManager.Instance;
        if (match != null) match.SetBotCountRpc(match.BotCount + delta);
    }

    void OnReadyClicked()
    {
        if (local != null) local.RequestReady(!local.IsReady);
    }

    static bool PlayPressed() =>
        (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) ||
        (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame);

    static bool EnterPressed() =>
        Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);

    // ---- helpers ------------------------------------------------------------------------------

    void RefreshReadyList()
    {
        var sb = new StringBuilder();
        int ready = 0, total = 0;
        var players = MatchManager.ActivePlayers(true);
        // People first (by id), then the bots in order.
        players.Sort((a, b) =>
        {
            if (a.IsBot != b.IsBot) return a.IsBot ? 1 : -1;
            return a.IsBot ? a.BotNumber.CompareTo(b.BotNumber) : a.OwnerClientId.CompareTo(b.OwnerClientId);
        });

        foreach (var p in players)
        {
            total++;
            if (p.IsReady) ready++;
            string color = ColorUtility.ToHtmlStringRGB(PlayerAppearance.ColorFor(p.ColorId));
            string you = p == local ? " (вы)" : "";
            string status = p.IsReady ? "<color=#7CFF8A>готов</color>" : "<color=#BBBBBB>ждём…</color>";
            sb.Append($"<color=#{color}>{p.DisplayName}</color>{you} — {status}\n");
        }
        readyPlayers.text = sb.ToString() + $"\nГотовы: {ready} из {total}";
    }

    static void SetShown(RectTransform rt, bool shown)
    {
        if (rt.gameObject.activeSelf != shown) rt.gameObject.SetActive(shown);
    }

    // ---- building the UI ----------------------------------------------------------------------

    void BuildUI()
    {
        var canvas = UIKit.CreateCanvas(transform, "MatchCanvas", 80);
        Transform root = canvas.transform;

        // "Play" prompt at the bottom.
        playPrompt = UIKit.AddPanel(root, "PlayPrompt", new Vector2(560f, 250f));
        playPrompt.anchorMin = playPrompt.anchorMax = new Vector2(0.5f, 0f);
        playPrompt.pivot = new Vector2(0.5f, 0f);
        playPrompt.anchoredPosition = new Vector2(0f, 70f);
        promptTitle = UIKit.AddLabel(playPrompt, "Всё готово к бою?", 34, FontStyle.Bold, Color.white, 50f);
        UIKit.AddButton(playPrompt, "ИГРАТЬ   [E]", UIKit.Green, OnPlayClicked, 92f, 44);
        UIKit.AddLabel(playPrompt, "Чтобы нажать мышью, зажмите Alt", 22, FontStyle.Normal, new Color(1f, 1f, 1f, 0.6f), 34f);
        playPrompt.gameObject.SetActive(false);

        // Ready menu.
        readyPanel = UIKit.AddPanel(root, "ReadyPanel", new Vector2(760f, 830f));
        readyTitle = UIKit.AddLabel(readyPanel, "БОЙ", 70, FontStyle.Bold, Color.white, 100f);
        UIKit.AddLabel(readyPanel, "Готов играть?", 40, FontStyle.Normal, new Color(1f, 1f, 1f, 0.85f), 56f);
        readyPlayers = UIKit.AddLabel(readyPanel, "", 34, FontStyle.Normal, Color.white, 290f, TextAnchor.UpperCenter);
        readyPlayers.supportRichText = true;

        // Host only, and only when fighting bots: "-  Ботов: 2  +".
        botRow = UIKit.NewUI("BotRow", readyPanel);
        var row = botRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 24f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        botRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;
        botMinus = UIKit.AddButton(botRow, "-", UIKit.Gray, () => ChangeBots(-1), 84f, 56);
        botMinus.GetComponent<LayoutElement>().preferredWidth = 110f;
        botLabel = UIKit.AddLabel(botRow, "Ботов: 1", 42, FontStyle.Bold, Color.white, 84f);
        botLabel.gameObject.GetComponent<LayoutElement>().preferredWidth = 330f;
        botPlus = UIKit.AddButton(botRow, "+", UIKit.Gray, () => ChangeBots(1), 84f, 56);
        botPlus.GetComponent<LayoutElement>().preferredWidth = 110f;
        botRow.gameObject.SetActive(false);

        readyButton = UIKit.AddButton(readyPanel, "Готов", UIKit.Green, OnReadyClicked, 92f, 44);
        UIKit.AddLabel(readyPanel, "Enter — тоже нажимает кнопку", 22, FontStyle.Normal, new Color(1f, 1f, 1f, 0.55f), 34f);
        readyPanel.gameObject.SetActive(false);

        // Countdown / FIGHT!
        bigText = UIKit.AddLabel(root, "", 260, FontStyle.Bold, Color.white, 300f);
        var bigRect = bigText.rectTransform;
        bigRect.anchorMin = bigRect.anchorMax = bigRect.pivot = new Vector2(0.5f, 0.5f);
        bigRect.sizeDelta = new Vector2(1200f, 340f);
        bigText.horizontalOverflow = HorizontalWrapMode.Overflow;
        bigText.verticalOverflow = VerticalWrapMode.Overflow;
        AddOutline(bigText, 8f);
        bigRect.gameObject.SetActive(false);

        // Health bar + info line at the bottom.
        hud = UIKit.NewUI("Hud", root);
        hud.anchorMin = hud.anchorMax = new Vector2(0.5f, 0f);
        hud.pivot = new Vector2(0.5f, 0f);
        hud.anchoredPosition = new Vector2(-120f, 50f); // a bit left of centre: the ability slots take the bottom right
        hud.sizeDelta = new Vector2(640f, 110f);

        infoText = UIKit.AddLabel(hud, "", 30, FontStyle.Bold, Color.white, 40f);
        var infoRect = infoText.rectTransform;
        infoRect.anchorMin = new Vector2(0f, 1f);
        infoRect.anchorMax = new Vector2(1f, 1f);
        infoRect.pivot = new Vector2(0.5f, 1f);
        infoRect.anchoredPosition = Vector2.zero;
        infoRect.sizeDelta = new Vector2(0f, 40f);
        infoText.horizontalOverflow = HorizontalWrapMode.Overflow;
        AddOutline(infoText, 3f);

        var barBack = UIKit.NewUI("BarBack", hud);
        barBack.anchorMin = new Vector2(0f, 0f);
        barBack.anchorMax = new Vector2(1f, 0f);
        barBack.pivot = new Vector2(0.5f, 0f);
        barBack.anchoredPosition = Vector2.zero;
        barBack.sizeDelta = new Vector2(0f, 48f);
        barBack.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

        hpFill = UIKit.NewUI("BarFill", barBack);
        hpFill.anchorMin = Vector2.zero;
        hpFill.anchorMax = Vector2.one;
        hpFill.offsetMin = new Vector2(4f, 4f);
        hpFill.offsetMax = new Vector2(-4f, -4f);
        hpFill.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.85f, 0.3f);

        hpText = UIKit.AddLabel(barBack, "", 30, FontStyle.Bold, Color.white, 48f);
        UIKit.Stretch(hpText.rectTransform);
        AddOutline(hpText, 3f);
        hud.gameObject.SetActive(false);

        // "You're out" banner.
        knockedOutText = UIKit.AddLabel(root, "Вы выбыли", 90, FontStyle.Bold, new Color(1f, 0.35f, 0.3f), 120f);
        var koRect = knockedOutText.rectTransform;
        koRect.anchorMin = koRect.anchorMax = new Vector2(0.5f, 1f);
        koRect.pivot = new Vector2(0.5f, 1f);
        koRect.anchoredPosition = new Vector2(0f, -80f);
        koRect.sizeDelta = new Vector2(1000f, 130f);
        knockedOutText.horizontalOverflow = HorizontalWrapMode.Overflow;
        AddOutline(knockedOutText, 5f);
        koRect.gameObject.SetActive(false);

        // Winner screen.
        resultPanel = UIKit.NewUI("Result", root);
        resultPanel.anchorMin = resultPanel.anchorMax = resultPanel.pivot = new Vector2(0.5f, 0.5f);
        resultPanel.sizeDelta = new Vector2(1400f, 300f);
        resultTitle = UIKit.AddLabel(resultPanel, "", 120, FontStyle.Bold, new Color(1f, 0.85f, 0.3f), 160f);
        var titleRect = resultTitle.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 0.4f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.offsetMin = titleRect.offsetMax = Vector2.zero;
        AddOutline(resultTitle, 6f);
        resultSub = UIKit.AddLabel(resultPanel, "", 44, FontStyle.Bold, Color.white, 60f);
        var subRect = resultSub.rectTransform;
        subRect.anchorMin = new Vector2(0f, 0f);
        subRect.anchorMax = new Vector2(1f, 0.4f);
        subRect.offsetMin = subRect.offsetMax = Vector2.zero;
        AddOutline(resultSub, 4f);
        resultPanel.gameObject.SetActive(false);

        // Loading fade, above everything (pause menu is 100, main menu 90).
        var fadeCanvas = UIKit.CreateCanvas(transform, "FadeCanvas", 200);
        var fadeRect = UIKit.NewUI("Fade", fadeCanvas.transform);
        UIKit.Stretch(fadeRect);
        fade = fadeRect.gameObject.AddComponent<Image>();
        fade.raycastTarget = false;
        fadeText = UIKit.AddLabel(fadeCanvas.transform, "Загрузка…", 64, FontStyle.Bold, Color.white, 100f);
        var fadeTextRect = fadeText.rectTransform;
        fadeTextRect.anchorMin = fadeTextRect.anchorMax = fadeTextRect.pivot = new Vector2(0.5f, 0.5f);
        fadeTextRect.sizeDelta = new Vector2(900f, 120f);
        fadeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        SetFade(0f);
    }

    static void AddOutline(Text text, float distance)
    {
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(distance, -distance);
    }
}
