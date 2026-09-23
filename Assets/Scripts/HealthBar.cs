using UnityEngine;
using UnityEngine.UI;

// A health bar floating above a fighter's head, only shown during a match. It is built in code when the player
// spawns, and always turns to face the camera.
[RequireComponent(typeof(PlayerController))]
public class HealthBar : MonoBehaviour
{
    const float Width = 120f, Height = 14f;   // in canvas pixels; the canvas is scaled to 0.01 so this is 1.2 m x 14 cm
    const float HeightAbove = 2.5f;

    PlayerController player;
    Transform canvasTransform;
    GameObject canvasObject;
    RectTransform fill;
    Image fillImage;

    void Awake()
    {
        player = GetComponent<PlayerController>();

        canvasObject = new GameObject("HealthBar", typeof(Canvas));
        canvasTransform = canvasObject.transform;
        canvasTransform.SetParent(transform, false);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvasTransform).sizeDelta = new Vector2(Width, Height);
        canvasTransform.localScale = Vector3.one * 0.01f;

        var back = UIKit.NewUI("Back", canvasTransform);
        UIKit.Stretch(back);
        back.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        // The fill sits inside a small border; its width is changed through the anchors.
        fill = UIKit.NewUI("Fill", canvasTransform);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = new Vector2(2f, 2f);
        fill.offsetMax = new Vector2(-2f, -2f);
        fillImage = fill.gameObject.AddComponent<Image>();

        canvasObject.SetActive(false);
    }

    void LateUpdate()
    {
        bool show = player.IsSpawned && player.InMatch && player.Health > 0;
        if (canvasObject.activeSelf != show) canvasObject.SetActive(show);
        if (!show) return;

        float ratio = Mathf.Clamp01(player.Health / (float)PlayerController.MaxHealth);
        fill.anchorMax = new Vector2(ratio, 1f);
        fillImage.color = Color.Lerp(new Color(0.9f, 0.15f, 0.15f), new Color(0.25f, 0.9f, 0.3f), ratio);

        canvasTransform.position = transform.position + Vector3.up * HeightAbove;
        var cam = Camera.main;
        if (cam != null)
            canvasTransform.rotation = Quaternion.LookRotation(canvasTransform.position - cam.transform.position);
    }
}
