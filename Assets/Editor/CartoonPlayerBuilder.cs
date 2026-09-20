using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Rebuilds the whole game scene from code: ground, fence and stands, punching bag, the color zone, slime spawner,
// network + menu objects, camera, and the networked Player and Slime prefabs (spawned by Netcode when a game starts).
public static class CartoonPlayerBuilder
{
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    const string BotPrefabPath = "Assets/Prefabs/Bot.prefab";
    const string MobPrefabPath = "Assets/Prefabs/Slime.prefab";
    static readonly Vector3 BagAnchor = new Vector3(6.3f, 0f, 5.9f);    // ground below the hanging punching bag
    static readonly Vector3 ZoneCenter = new Vector3(5f, 0f, 14.5f);    // the glowing pad, a good way from the bag
    static readonly Vector3 BotZoneCenter = new Vector3(-5f, 0f, 14.5f); // the second pad, "play with a bot"
    const float CameraDistance = 9.5f;   // how far the camera hangs behind the character
    const float FenceHalfSize = 30f;     // the fence is a 60 x 60 m square around the middle
    const float GateHalfWidth = 2f;      // gap in the south fence, so players can walk out to the stands

    [MenuItem("Tools/Rebuild Game Scene")]
    public static void Build()
    {
        // Rebuild from scratch so the menu item is safe to run more than once.
        // ("House" is listed only to clear it out of scenes built before it was replaced by the color zone.)
        foreach (var name in new[] { "Player", "Ground", "House", "PunchingBagRig", "ColorZone", "BotZone", "Arena", "RingArena", "RingFloorRoot", "RingFence",
                                     "PauseMenu", "MainMenu", "MatchUI", "AbilityUI", "NetworkGame", "HitEffects", "MobSpawner" })
            RemoveExisting(name);

        Material groundMat = MakeMaterial("Ground", new Color(0.45f, 0.75f, 0.4f));

        // Ground: 80 x 80 m. The fence encloses the middle 60 x 60; the stands stand on the rest.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(8f, 1f, 8f);
        ground.GetComponent<Renderer>().sharedMaterial = groundMat;
        Undo.RegisterCreatedObjectUndo(ground, "Create Ground");

        GameObject playerPrefab = BuildPlayerPrefab();
        GameObject mobPrefab = BuildMobPrefab();
        BuildPunchingBag(BagAnchor);
        BuildColorZone(ZoneCenter, "ColorZone", null, MatchMode.Players);
        BuildColorZone(BotZoneCenter, "BotZone", "ИГРАТЬ\nС БОТОМ", MatchMode.Bots);
        BuildStartField();
        BuildRing();
        BuildEffectsAndSpawner(mobPrefab);
        BuildNetworkObjects(playerPrefab, BuildMatchManagerPrefab(BuildBotPrefab()));

        // Camera: orbits the scene in the menu, follows the local player once one spawns.
        Camera cam = Camera.main;
        if (cam != null)
        {
            var follow = cam.GetComponent<CameraFollow>();
            if (follow == null) follow = cam.gameObject.AddComponent<CameraFollow>();
            // The distance is stored in the scene, so a new default in the script isn't enough - set it here.
            var followSo = new SerializedObject(follow);
            followSo.FindProperty("distance").floatValue = CameraDistance;
            followSo.ApplyModifiedPropertiesWithoutUndo();
            cam.transform.position = new Vector3(0f, 3.5f, -6.5f);
            cam.transform.LookAt(Vector3.up);
        }
        else
        {
            Debug.LogWarning("No camera tagged MainCamera found - add CameraFollow to your camera manually.");
        }

        AssetDatabase.SaveAssets();
        var scene = ground.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Game scene rebuilt. Press Play: main menu -> solo / host / join. WASD move, Shift sprint, Space jump, right mouse punch, Esc menu.");
    }

    // The character as a prefab: physics + networking components live on the root, animated parts below.
    static GameObject BuildPlayerPrefab() => BuildCharacterPrefab(PlayerPrefabPath, "Player", false);

    // The very same character, with a BotBrain that presses the buttons instead of a keyboard.
    static GameObject BuildBotPrefab() => BuildCharacterPrefab(BotPrefabPath, "Bot", true);

    static GameObject BuildCharacterPrefab(string path, string objectName, bool bot)
    {
        Material bodyMat = MakeMaterial("PlayerBody", new Color(1f, 0.55f, 0.2f));
        Material footMat = MakeMaterial("PlayerFoot", new Color(0.55f, 0.25f, 0.1f));
        Material eyeMat = MakeMaterial("PlayerEye", Color.white);
        Material pupilMat = MakeMaterial("PlayerPupil", new Color(0.05f, 0.05f, 0.05f));

        var player = new GameObject(objectName);
        player.AddComponent<NetworkObject>();
        if (bot) player.AddComponent<BotBrain>();

        var controller = player.AddComponent<CharacterController>();
        controller.height = 1.5f;
        controller.radius = 0.5f;
        controller.center = new Vector3(0f, 0.75f, 0f);
        var playerController = player.AddComponent<PlayerController>();

        var netTransform = player.AddComponent<ClientNetworkTransform>();
        netTransform.SyncScaleX = netTransform.SyncScaleY = netTransform.SyncScaleZ = false;
        netTransform.SyncRotAngleX = netTransform.SyncRotAngleZ = false; // characters only turn around Y

        player.AddComponent<HitFlash>();
        player.AddComponent<HealthBar>();

        // The five ability cards (input + host-side effects) and what they look like on the character.
        player.AddComponent<PlayerAbilities>();
        var visuals = player.AddComponent<AbilityVisuals>();
        var visualsSo = new SerializedObject(visuals);
        visualsSo.FindProperty("spikeMaterial").objectReferenceValue = MakeMaterial("Spike", new Color(0.72f, 0.76f, 0.85f));
        visualsSo.ApplyModifiedPropertiesWithoutUndo();

        var model = new GameObject("Model").transform;
        model.SetParent(player.transform, false);

        // Body pivot (animated by CartoonWalker); the round mesh and eyes are its children.
        var body = new GameObject("Body").transform;
        body.SetParent(model, false);
        Transform bodyMesh = Part("BodyMesh", body, new Vector3(0f, 0.75f, 0f), new Vector3(1.3f, 1.2f, 1.2f), bodyMat);
        Part("EyeL", body, new Vector3(-0.2f, 0.9f, 0.5f), Vector3.one * 0.3f, eyeMat);
        Part("EyeR", body, new Vector3(0.2f, 0.9f, 0.5f), Vector3.one * 0.3f, eyeMat);
        Part("PupilL", body, new Vector3(-0.2f, 0.9f, 0.62f), Vector3.one * 0.14f, pupilMat);
        Part("PupilR", body, new Vector3(0.2f, 0.9f, 0.62f), Vector3.one * 0.14f, pupilMat);

        Transform footL = Part("FootL", model, new Vector3(-0.28f, 0.13f, 0.05f), new Vector3(0.4f, 0.26f, 0.55f), footMat);
        Transform footR = Part("FootR", model, new Vector3(0.28f, 0.13f, 0.05f), new Vector3(0.4f, 0.26f, 0.55f), footMat);
        Transform handL = Part("HandL", model, new Vector3(-0.72f, 0.7f, 0f), Vector3.one * 0.28f, bodyMat);
        Transform handR = Part("HandR", model, new Vector3(0.72f, 0.7f, 0f), Vector3.one * 0.28f, bodyMat);

        var walker = player.AddComponent<CartoonWalker>();
        var so = new SerializedObject(walker);
        so.FindProperty("player").objectReferenceValue = playerController;
        so.FindProperty("body").objectReferenceValue = body;
        so.FindProperty("leftFoot").objectReferenceValue = footL;
        so.FindProperty("rightFoot").objectReferenceValue = footR;
        so.FindProperty("leftHand").objectReferenceValue = handL;
        so.FindProperty("rightHand").objectReferenceValue = handR;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Body and hands get a per-player color at runtime.
        var appearance = player.AddComponent<PlayerAppearance>();
        var appearanceSo = new SerializedObject(appearance);
        var tinted = appearanceSo.FindProperty("tinted");
        var renderers = new List<Renderer>
        {
            bodyMesh.GetComponent<Renderer>(), handL.GetComponent<Renderer>(), handR.GetComponent<Renderer>()
        };
        tinted.arraySize = renderers.Count;
        for (int i = 0; i < renderers.Count; i++)
            tinted.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        appearanceSo.ApplyModifiedPropertiesWithoutUndo();

        return SavePrefab(player, path);
    }

    // A wandering slime: host-controlled, hittable, respawns when knocked out.
    static GameObject BuildMobPrefab()
    {
        Material bodyMat = MakeMaterial("SlimeBody", new Color(0.4f, 0.85f, 0.45f));
        Material eyeMat = MakeMaterial("PlayerEye", Color.white);
        Material pupilMat = MakeMaterial("PlayerPupil", new Color(0.05f, 0.05f, 0.05f));

        var slime = new GameObject("Slime");
        slime.AddComponent<NetworkObject>();

        var controller = slime.AddComponent<CharacterController>();
        controller.height = 0.9f;
        controller.radius = 0.45f;
        controller.center = new Vector3(0f, 0.45f, 0f);

        var netTransform = slime.AddComponent<NetworkTransform>(); // host authoritative
        netTransform.SyncScaleX = netTransform.SyncScaleY = netTransform.SyncScaleZ = false;
        netTransform.SyncRotAngleX = netTransform.SyncRotAngleZ = false;

        slime.AddComponent<HitFlash>();
        var mob = slime.AddComponent<Mob>();

        // "Visual" is what Mob animates (hops, squashes); it sits at ground level so it squashes from the floor.
        var visual = new GameObject("Visual").transform;
        visual.SetParent(slime.transform, false);
        Part("Body", visual, new Vector3(0f, 0.4f, 0f), new Vector3(0.95f, 0.8f, 0.95f), bodyMat);
        Part("EyeL", visual, new Vector3(-0.17f, 0.5f, 0.36f), Vector3.one * 0.22f, eyeMat);
        Part("EyeR", visual, new Vector3(0.17f, 0.5f, 0.36f), Vector3.one * 0.22f, eyeMat);
        Part("PupilL", visual, new Vector3(-0.17f, 0.5f, 0.45f), Vector3.one * 0.1f, pupilMat);
        Part("PupilR", visual, new Vector3(0.17f, 0.5f, 0.45f), Vector3.one * 0.1f, pupilMat);

        var mobSo = new SerializedObject(mob);
        mobSo.FindProperty("visual").objectReferenceValue = visual;
        mobSo.FindProperty("maxHealth").intValue = 20; // a punch does 5 damage: still 4 hits per slime
        mobSo.ApplyModifiedPropertiesWithoutUndo();

        return SavePrefab(slime, MobPrefabPath);
    }

    // The network object that runs a fight (phase, countdown, winner); the host spawns one when a game starts.
    static GameObject BuildMatchManagerPrefab(GameObject botPrefab)
    {
        var go = new GameObject("MatchManager");
        go.AddComponent<NetworkObject>();
        var manager = go.AddComponent<MatchManager>();
        var so = new SerializedObject(manager);
        so.FindProperty("botPrefab").objectReferenceValue = botPrefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        return SavePrefab(go, "Assets/Prefabs/MatchManager.prefab");
    }

    static GameObject SavePrefab(GameObject instance, string path)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
        Object.DestroyImmediate(instance);
        return prefab;
    }

    // A wooden gallows-style stand with a swinging bag, plus a hit counter on a signpost next to it.
    // `anchor` is the spot on the ground right below the hanging bag.
    static void BuildPunchingBag(Vector3 anchor)
    {
        Material wood = MakeMaterial("BagWood", new Color(0.55f, 0.35f, 0.2f));
        Material metal = MakeMaterial("BagMetal", new Color(0.25f, 0.25f, 0.28f));
        Material leather = MakeMaterial("BagLeather", new Color(0.75f, 0.15f, 0.15f));
        Material strap = MakeMaterial("BagStrap", new Color(0.15f, 0.1f, 0.1f));
        Material board = MakeMaterial("SignBoard", new Color(0.2f, 0.15f, 0.1f));

        var rig = new GameObject("PunchingBagRig");
        rig.transform.position = Vector3.zero;
        Transform t = rig.transform;

        // Free-standing frame; the bag hangs from the arm, which reaches out from the post towards -X.
        Vector3 pivot = new Vector3(anchor.x, 2.09f, anchor.z);
        float postX = anchor.x + 1.2f;
        Box("Base", t, new Vector3(postX, 0.04f, pivot.z), new Vector3(0.9f, 0.08f, 0.9f), wood);
        Box("Post", t, new Vector3(postX, 1.2f, pivot.z), new Vector3(0.16f, 2.4f, 0.16f), wood, keepCollider: true);
        Box("Arm", t, new Vector3((postX + pivot.x) / 2f, 2.15f, pivot.z), new Vector3(postX - pivot.x + 0.1f, 0.12f, 0.12f), wood);
        Transform brace = Box("Brace", t, new Vector3(postX - 0.25f, 1.825f, pivot.z), new Vector3(0.82f, 0.08f, 0.08f), wood);
        brace.localRotation = Quaternion.Euler(0f, 0f, 127.6f);
        Part("Hook", t, pivot + Vector3.up * 0.03f, Vector3.one * 0.1f, metal);

        // The bag: PunchingBag script at the pivot; Swing rotates; Bag holds the collider; Visual is squashed.
        var bagRoot = new GameObject("PunchingBag");
        bagRoot.transform.SetParent(t, false);
        bagRoot.transform.position = pivot;

        var swing = new GameObject("Swing").transform;
        swing.SetParent(bagRoot.transform, false);

        var chain = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        chain.name = "Chain";
        Object.DestroyImmediate(chain.GetComponent<Collider>());
        chain.GetComponent<Renderer>().sharedMaterial = metal;
        chain.transform.SetParent(swing, false);
        chain.transform.localPosition = new Vector3(0f, -0.125f, 0f);
        chain.transform.localScale = new Vector3(0.04f, 0.125f, 0.04f);

        var bag = new GameObject("Bag").transform;
        bag.SetParent(swing, false);
        bag.localPosition = new Vector3(0f, -0.9f, 0f);
        var capsule = bag.gameObject.AddComponent<CapsuleCollider>();
        capsule.radius = 0.3f;
        capsule.height = 1.3f;

        var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Visual";
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.GetComponent<Renderer>().sharedMaterial = leather;
        visual.transform.SetParent(bag, false);
        visual.transform.localScale = new Vector3(0.6f, 0.65f, 0.6f);
        foreach (float y in new[] { -0.55f, 0.55f })
        {
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            band.name = "Strap";
            Object.DestroyImmediate(band.GetComponent<Collider>());
            band.GetComponent<Renderer>().sharedMaterial = strap;
            band.transform.SetParent(visual.transform, false);
            band.transform.localPosition = new Vector3(0f, y, 0f);
            band.transform.localScale = new Vector3(1.07f, 0.046f, 1.07f);
        }

        // Hit counter on a signpost beside the stand, facing -Z where players come from.
        float signX = postX + 1.8f;
        Box("SignLegL", t, new Vector3(signX - 0.75f, 0.65f, anchor.z), new Vector3(0.08f, 1.3f, 0.08f), wood);
        Box("SignLegR", t, new Vector3(signX + 0.75f, 0.65f, anchor.z), new Vector3(0.08f, 1.3f, 0.08f), wood);
        Box("SignBoard", t, new Vector3(signX, 1.55f, anchor.z), new Vector3(1.9f, 0.5f, 0.05f), board);
        var canvasGo = new GameObject("CounterCanvas", typeof(Canvas));
        canvasGo.transform.SetParent(t, false);
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        canvasGo.transform.position = new Vector3(signX, 1.55f, anchor.z - 0.05f);
        ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(380f, 100f);
        canvasGo.transform.localScale = Vector3.one * 0.005f;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(canvasGo.transform, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        var counter = textGo.AddComponent<Text>();
        counter.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        counter.text = "Ударов: 0";
        counter.fontSize = 60;
        counter.fontStyle = FontStyle.Bold;
        counter.alignment = TextAnchor.MiddleCenter;
        counter.color = new Color(1f, 0.85f, 0.3f);

        var flash = bagRoot.AddComponent<HitFlash>();
        var punchingBag = bagRoot.AddComponent<PunchingBag>();
        var so = new SerializedObject(punchingBag);
        so.FindProperty("swing").objectReferenceValue = swing;
        so.FindProperty("visual").objectReferenceValue = visual.transform;
        so.FindProperty("counterText").objectReferenceValue = counter;
        so.FindProperty("flash").objectReferenceValue = flash;
        so.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(rig, "Create Punching Bag");
    }

    // A 6 x 6 m pad of 100 small glowing tiles (ColorZone recolors them every frame), a point light above it
    // and a big text floating over it: the number of players standing on the pad, or - when `caption` is given -
    // that caption instead. `mode` is what pressing "Play" on this pad starts.
    static void BuildColorZone(Vector3 center, string objectName, string caption, MatchMode mode)
    {
        bool hasCaption = !string.IsNullOrEmpty(caption);

        // Unlit, so the tiles look like they glow whatever the sun is doing.
        Material tileMat = MakeMaterial("ZoneTile", Color.white, "Universal Render Pipeline/Unlit");
        Material baseMat = MakeMaterial("ZoneBase", new Color(0.08f, 0.08f, 0.1f));

        var zone = new GameObject(objectName);
        zone.transform.position = center;
        Transform t = zone.transform;

        // Dark base and rim, so the gaps between tiles read as thin dark lines.
        // (Everything below is positioned relative to the zone root, which sits at `center`.)
        Box("Base", t, new Vector3(0f, 0.03f, 0f), new Vector3(6.2f, 0.06f, 6.2f), baseMat);
        foreach (float s in new[] { -1f, 1f })
        {
            Box("RimX", t, new Vector3(s * 3.1f, 0.08f, 0f), new Vector3(0.2f, 0.16f, 6.4f), baseMat);
            Box("RimZ", t, new Vector3(0f, 0.08f, s * 3.1f), new Vector3(6.4f, 0.16f, 0.2f), baseMat);
        }

        const int grid = 10;
        const float cell = 0.6f;
        var tiles = new List<Renderer>();
        for (int i = 0; i < grid; i++)
        {
            for (int j = 0; j < grid; j++)
            {
                Vector3 offset = new Vector3((i - (grid - 1) / 2f) * cell, 0.06f, (j - (grid - 1) / 2f) * cell);
                Transform tile = Box("Tile", t, offset, new Vector3(cell - 0.04f, 0.1f, cell - 0.04f), tileMat);
                tiles.Add(tile.GetComponent<Renderer>());
            }
        }

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(t, false);
        lightGo.transform.position = center + Vector3.up * 2.5f;
        var glow = lightGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.range = 12f;
        glow.intensity = 2f;
        glow.shadows = LightShadows.None;

        // The number: a world-space canvas that ColorZone turns to face the camera.
        var anchor = new GameObject("CountAnchor", typeof(Canvas));
        anchor.transform.SetParent(t, false);
        anchor.transform.position = center + Vector3.up * 4.6f;
        anchor.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)anchor.transform).sizeDelta = hasCaption ? new Vector2(560f, 300f) : new Vector2(300f, 300f);
        anchor.transform.localScale = Vector3.one * 0.012f; // 300 px -> 3.6 m

        var textGo = new GameObject("Count", typeof(RectTransform));
        textGo.transform.SetParent(anchor.transform, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        var count = textGo.AddComponent<Text>();
        count.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        count.text = hasCaption ? caption : "0";
        count.fontSize = hasCaption ? 120 : 250;
        count.fontStyle = FontStyle.Bold;
        count.alignment = TextAnchor.MiddleCenter;
        count.horizontalOverflow = HorizontalWrapMode.Overflow;
        count.verticalOverflow = VerticalWrapMode.Overflow;
        count.color = Color.white;
        var outline = textGo.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(8f, -8f);

        var colorZone = zone.AddComponent<ColorZone>();
        var so = new SerializedObject(colorZone);
        var tilesProp = so.FindProperty("tiles");
        tilesProp.arraySize = tiles.Count;
        for (int i = 0; i < tiles.Count; i++)
            tilesProp.GetArrayElementAtIndex(i).objectReferenceValue = tiles[i];
        so.FindProperty("halfSize").vector2Value = new Vector2(grid * cell / 2f, grid * cell / 2f);
        so.FindProperty("countAnchor").objectReferenceValue = anchor.transform;
        so.FindProperty("countText").objectReferenceValue = count;
        so.FindProperty("glow").objectReferenceValue = glow;
        so.FindProperty("mode").enumValueIndex = (int)mode;
        so.FindProperty("caption").stringValue = hasCaption ? caption : "";
        so.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(zone, "Create Color Zone");
    }

    // Spark particles, and the host-side slime spawner.
    static void BuildEffectsAndSpawner(GameObject mobPrefab)
    {
        Material spark = MakeMaterial("HitSpark", Color.white, "Universal Render Pipeline/Particles/Unlit");

        var fxGo = new GameObject("HitEffects");
        var fxSo = new SerializedObject(fxGo.AddComponent<HitEffects>());
        fxSo.FindProperty("sparkMaterial").objectReferenceValue = spark;
        fxSo.ApplyModifiedPropertiesWithoutUndo();
        Undo.RegisterCreatedObjectUndo(fxGo, "Create Hit Effects");

        var spawnerGo = new GameObject("MobSpawner");
        var spawner = spawnerGo.AddComponent<MobSpawner>();
        var spawnerSo = new SerializedObject(spawner);
        spawnerSo.FindProperty("mobPrefab").objectReferenceValue = mobPrefab;
        var homes = spawnerSo.FindProperty("homes");
        var positions = new[]
        {
            new Vector3(-8f, 0f, 6f), new Vector3(10f, 0f, -4f), new Vector3(-4f, 0f, 16f), new Vector3(14f, 0f, 4f),
            new Vector3(-18f, 0f, -12f), new Vector3(20f, 0f, 20f)
        };
        homes.arraySize = positions.Length;
        for (int i = 0; i < positions.Length; i++)
            homes.GetArrayElementAtIndex(i).vector3Value = positions[i];
        spawnerSo.ApplyModifiedPropertiesWithoutUndo();
        Undo.RegisterCreatedObjectUndo(spawnerGo, "Create Mob Spawner");
    }

    // The start field: a 60 x 60 m fence with a gate in the south, and walkable stands with seats outside it.
    static void BuildStartField()
    {
        Material railMat = MakeMaterial("FenceRail", new Color(0.72f, 0.55f, 0.35f));
        Material postMat = MakeMaterial("FencePost", new Color(0.5f, 0.35f, 0.2f));
        Material stepMat = MakeMaterial("StandStep", new Color(0.72f, 0.72f, 0.76f));
        Material seatMat = MakeMaterial("StandSeat", new Color(0.2f, 0.45f, 0.8f));

        BuildFencedArena("Arena", Vector3.zero, FenceHalfSize, true, 40f, railMat, postMat, stepMat, seatMat);
    }

    // The fight ring, far away from the start field: a smaller (24 x 24 m) fenced square with NO gate, so nobody
    // can walk out, plus a floor with a red border, its own ground and stands all the way round.
    static void BuildRing()
    {
        Vector3 c = GameLayout.RingCenter;
        float half = GameLayout.RingHalfSize;

        Material groundMat = MakeMaterial("RingGround", new Color(0.35f, 0.4f, 0.5f));
        Material floorMat = MakeMaterial("RingFloor", new Color(0.72f, 0.75f, 0.82f));
        Material borderMat = MakeMaterial("RingBorder", new Color(0.85f, 0.15f, 0.15f));
        Material ropeMat = MakeMaterial("RingRope", new Color(0.85f, 0.15f, 0.15f));
        Material postMat = MakeMaterial("RingPost", new Color(0.25f, 0.25f, 0.3f));
        Material stepMat = MakeMaterial("RingStep", new Color(0.3f, 0.3f, 0.38f));
        Material seatMat = MakeMaterial("RingSeat", new Color(0.95f, 0.55f, 0.15f));

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "RingArena";
        ground.transform.position = c;
        ground.transform.localScale = new Vector3(5f, 1f, 5f);
        ground.GetComponent<Renderer>().sharedMaterial = groundMat;
        Undo.RegisterCreatedObjectUndo(ground, "Create Ring Ground");

        // The floor sits 10 cm up (low enough to just walk onto), with a red border around it.
        // It gets its own root: children of the ground plane would inherit its 5x scale.
        var floorRoot = new GameObject("RingFloorRoot");
        floorRoot.transform.position = c;
        Transform f = floorRoot.transform;
        Box("Floor", f, new Vector3(0f, 0.05f, 0f), new Vector3(half * 2f - 0.8f, 0.1f, half * 2f - 0.8f), floorMat);
        foreach (float s in new[] { -1f, 1f })
        {
            Box("BorderX", f, new Vector3(s * (half - 0.2f), 0.06f, 0f), new Vector3(0.4f, 0.12f, half * 2f), borderMat);
            Box("BorderZ", f, new Vector3(0f, 0.06f, s * (half - 0.2f)), new Vector3(half * 2f, 0.12f, 0.4f), borderMat);
        }
        // A red circle-ish marker in the middle so the ring reads as a fighting spot.
        Box("Center", f, new Vector3(0f, 0.11f, 0f), new Vector3(3f, 0.02f, 3f), borderMat);
        Undo.RegisterCreatedObjectUndo(floorRoot, "Create Ring Floor");

        BuildFencedArena("RingFence", c, half, false, 20f, ropeMat, postMat, stepMat, seatMat);
    }

    // A square fence around `center` (`half` = half the side length) with stands outside it.
    // With a gate, the south side has an opening and the stands leave a walkway to it.
    static void BuildFencedArena(string name, Vector3 center, float half, bool gate, float standLength,
        Material railMat, Material postMat, Material stepMat, Material seatMat)
    {
        var arena = new GameObject(name);
        Transform t = arena.transform;
        Vector3 c = center;
        float f = half;
        float g = GateHalfWidth;

        // Fence.
        FenceRun(t, c + new Vector3(-f, 0f, f), c + new Vector3(f, 0f, f), railMat, postMat);     // north
        FenceRun(t, c + new Vector3(f, 0f, f), c + new Vector3(f, 0f, -f), railMat, postMat);     // east
        FenceRun(t, c + new Vector3(-f, 0f, -f), c + new Vector3(-f, 0f, f), railMat, postMat);   // west
        if (gate)
        {
            FenceRun(t, c + new Vector3(-f, 0f, -f), c + new Vector3(-g, 0f, -f), railMat, postMat);  // south, left of the gate
            FenceRun(t, c + new Vector3(g, 0f, -f), c + new Vector3(f, 0f, -f), railMat, postMat);    // south, right of the gate

            // Gate: two tall posts and a beam over the opening.
            foreach (float x in new[] { -g, g })
                Box("GatePost", t, c + new Vector3(x, 1.2f, -f), new Vector3(0.3f, 2.4f, 0.3f), postMat);
            Box("GateBeam", t, c + new Vector3(0f, 2.4f, -f), new Vector3(g * 2f + 0.3f, 0.25f, 0.3f), postMat);
        }
        else
        {
            FenceRun(t, c + new Vector3(-f, 0f, -f), c + new Vector3(f, 0f, -f), railMat, postMat); // south, closed
        }

        // Stands outside the fence, leaving a 2 m walkway between them and the fence.
        float inner = f + 2f;
        StandRun(t, c + new Vector3(0f, 0f, inner), Vector3.right, Vector3.forward, standLength, stepMat, seatMat);     // north
        StandRun(t, c + new Vector3(inner, 0f, 0f), Vector3.forward, Vector3.right, standLength, stepMat, seatMat);     // east
        StandRun(t, c + new Vector3(-inner, 0f, 0f), Vector3.forward, Vector3.left, standLength, stepMat, seatMat);     // west
        if (gate)
        {
            // On the south side they stop short of the gate so players can walk straight out.
            StandRun(t, c + new Vector3(-14f, 0f, -inner), Vector3.right, Vector3.back, 20f, stepMat, seatMat);
            StandRun(t, c + new Vector3(14f, 0f, -inner), Vector3.right, Vector3.back, 20f, stepMat, seatMat);
        }
        else
        {
            StandRun(t, c + new Vector3(0f, 0f, -inner), Vector3.right, Vector3.back, standLength, stepMat, seatMat);
        }

        Undo.RegisterCreatedObjectUndo(arena, "Create " + name);
    }

    // Posts every ~3 m, two rails, and an invisible wall so nobody can hop over or squeeze between the rails.
    // The wall is on the "Ignore Raycast" layer so the camera can see and pass through the fence.
    static void FenceRun(Transform parent, Vector3 a, Vector3 b, Material railMat, Material postMat)
    {
        Vector3 delta = b - a;
        float length = delta.magnitude;
        Vector3 dir = delta / length;
        Quaternion rotation = Quaternion.LookRotation(dir);
        Vector3 middle = (a + b) / 2f;

        int segments = Mathf.CeilToInt(length / 3f);
        for (int i = 0; i <= segments; i++)
        {
            Vector3 p = a + dir * (length * i / segments);
            Box("Post", parent, p + Vector3.up * 0.65f, new Vector3(0.16f, 1.3f, 0.16f), postMat);
        }

        foreach (float y in new[] { 0.5f, 1.0f })
        {
            Transform rail = Box("Rail", parent, middle + Vector3.up * y, new Vector3(0.08f, 0.12f, length), railMat);
            rail.rotation = rotation;
        }

        var wall = new GameObject("FenceCollider");
        wall.layer = 2;
        wall.transform.SetParent(parent, false);
        wall.transform.SetPositionAndRotation(middle + Vector3.up * 1.5f, rotation);
        wall.AddComponent<BoxCollider>().size = new Vector3(0.3f, 3f, length);
    }

    // Three low steps (25 cm each, so characters can simply walk up) with rows of seats on them.
    // Seats come in 6 m blocks with 2 m gaps to walk up through. Everything is on the "Ignore Raycast" layer
    // so the camera isn't pushed around by it.
    static void StandRun(Transform parent, Vector3 innerCenter, Vector3 along, Vector3 outward, float length,
        Material stepMat, Material seatMat)
    {
        const float riser = 0.25f, depth = 1.5f, seatLength = 6f, seatGap = 2f;
        const int steps = 3;
        bool alongX = Mathf.Abs(along.x) > 0.5f;

        for (int i = 0; i < steps; i++)
        {
            float height = riser * (i + 1);
            Vector3 stepCenter = innerCenter + outward * (depth * i + depth / 2f) + Vector3.up * (height / 2f);
            Vector3 stepSize = alongX ? new Vector3(length, height, depth) : new Vector3(depth, height, length);
            Box("Step", parent, stepCenter, stepSize, stepMat, keepCollider: true).gameObject.layer = 2;

            int blocks = Mathf.FloorToInt((length + seatGap) / (seatLength + seatGap));
            float total = blocks * seatLength + (blocks - 1) * seatGap;
            float first = -total / 2f + seatLength / 2f;
            for (int k = 0; k < blocks; k++)
            {
                float offset = first + k * (seatLength + seatGap);
                Vector3 seatCenter = innerCenter + along * offset + outward * (depth * i + depth - 0.3f) + Vector3.up * (height + 0.2f);
                Vector3 seatSize = alongX ? new Vector3(seatLength, 0.4f, 0.45f) : new Vector3(0.45f, 0.4f, seatLength);
                Box("Seat", parent, seatCenter, seatSize, seatMat, keepCollider: true).gameObject.layer = 2;
            }
        }
    }

    // NetworkManager + transport + session logic, plus the menus.
    static void BuildNetworkObjects(GameObject playerPrefab, GameObject matchManagerPrefab)
    {
        var netGo = new GameObject("NetworkGame");
        var networkManager = netGo.AddComponent<NetworkManager>();
        var transport = netGo.AddComponent<UnityTransport>();
        if (networkManager.NetworkConfig == null) networkManager.NetworkConfig = new NetworkConfig();
        networkManager.NetworkConfig.NetworkTransport = transport;
        // Everyone already has the same single scene loaded; Netcode only needs to sync the players.
        networkManager.NetworkConfig.EnableSceneManagement = false;
        EditorUtility.SetDirty(networkManager);

        var game = netGo.AddComponent<NetworkGame>();
        var gameSo = new SerializedObject(game);
        gameSo.FindProperty("playerPrefab").objectReferenceValue = playerPrefab;
        gameSo.FindProperty("matchManagerPrefab").objectReferenceValue = matchManagerPrefab;
        gameSo.ApplyModifiedPropertiesWithoutUndo();
        Undo.RegisterCreatedObjectUndo(netGo, "Create NetworkGame");

        var mainMenu = new GameObject("MainMenu");
        mainMenu.AddComponent<MainMenu>();
        Undo.RegisterCreatedObjectUndo(mainMenu, "Create Main Menu");

        var matchUi = new GameObject("MatchUI");
        matchUi.AddComponent<MatchUI>();
        Undo.RegisterCreatedObjectUndo(matchUi, "Create Match UI");

        var abilityUi = new GameObject("AbilityUI");
        abilityUi.AddComponent<AbilityUI>();
        Undo.RegisterCreatedObjectUndo(abilityUi, "Create Ability UI");

        var pauseMenu = new GameObject("PauseMenu");
        pauseMenu.AddComponent<PauseMenu>();
        Undo.RegisterCreatedObjectUndo(pauseMenu, "Create Pause Menu");
    }

    static void RemoveExisting(string name)
    {
        var go = GameObject.Find(name);
        if (go != null) Undo.DestroyObjectImmediate(go);
    }

    static Transform Part(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        return go.transform;
    }

    static Transform Box(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        return go.transform;
    }

    static Material MakeMaterial(string name, Color color, string shaderName = "Universal Render Pipeline/Lit")
    {
        const string folder = "Assets/Materials";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "Materials");

        string path = $"{folder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find(shaderName));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
