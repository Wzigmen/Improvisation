using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Rebuilds the whole game scene from code: ground, house, network + menu objects, camera,
// and the networked Player prefab (spawned by Netcode when a game starts).
public static class CartoonPlayerBuilder
{
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

    [MenuItem("Tools/Rebuild Game Scene")]
    public static void Build()
    {
        // Rebuild from scratch so the menu item is safe to run more than once.
        foreach (var name in new[] { "Player", "Ground", "House", "PauseMenu", "MainMenu", "NetworkGame" })
            RemoveExisting(name);

        Material groundMat = MakeMaterial("Ground", new Color(0.45f, 0.75f, 0.4f));
        Mesh prism = SaveMesh("Assets/Meshes/Prism.asset", CreatePrismMesh());

        // Ground
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(5f, 1f, 5f);
        ground.GetComponent<Renderer>().sharedMaterial = groundMat;
        Undo.RegisterCreatedObjectUndo(ground, "Create Ground");

        GameObject playerPrefab = BuildPlayerPrefab();
        BuildHouse(prism, new Vector3(5f, 0f, 10f));
        BuildNetworkObjects(playerPrefab);

        // Camera: orbits the scene in the menu, follows the local player once one spawns.
        Camera cam = Camera.main;
        if (cam != null)
        {
            if (cam.GetComponent<CameraFollow>() == null) cam.gameObject.AddComponent<CameraFollow>();
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
        Debug.Log("Game scene rebuilt. Press Play: main menu -> solo / host / join. WASD move, Shift sprint, mouse look, Esc menu.");
    }

    // The character as a prefab: physics + networking components live on the root, animated parts below.
    static GameObject BuildPlayerPrefab()
    {
        Material bodyMat = MakeMaterial("PlayerBody", new Color(1f, 0.55f, 0.2f));
        Material footMat = MakeMaterial("PlayerFoot", new Color(0.55f, 0.25f, 0.1f));
        Material eyeMat = MakeMaterial("PlayerEye", Color.white);
        Material pupilMat = MakeMaterial("PlayerPupil", new Color(0.05f, 0.05f, 0.05f));

        var player = new GameObject("Player");
        player.AddComponent<NetworkObject>();

        var controller = player.AddComponent<CharacterController>();
        controller.height = 1.5f;
        controller.radius = 0.5f;
        controller.center = new Vector3(0f, 0.75f, 0f);
        var playerController = player.AddComponent<PlayerController>();

        var netTransform = player.AddComponent<ClientNetworkTransform>();
        netTransform.SyncScaleX = netTransform.SyncScaleY = netTransform.SyncScaleZ = false;
        netTransform.SyncRotAngleX = netTransform.SyncRotAngleZ = false; // characters only turn around Y

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

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
        Object.DestroyImmediate(player);
        return prefab;
    }

    // NetworkManager + transport + session logic, plus the menus.
    static void BuildNetworkObjects(GameObject playerPrefab)
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
        gameSo.ApplyModifiedPropertiesWithoutUndo();
        Undo.RegisterCreatedObjectUndo(netGo, "Create NetworkGame");

        var mainMenu = new GameObject("MainMenu");
        mainMenu.AddComponent<MainMenu>();
        Undo.RegisterCreatedObjectUndo(mainMenu, "Create Main Menu");

        var pauseMenu = new GameObject("PauseMenu");
        pauseMenu.AddComponent<PauseMenu>();
        Undo.RegisterCreatedObjectUndo(pauseMenu, "Create Pause Menu");
    }

    static void BuildHouse(Mesh prism, Vector3 position)
    {
        Material wallMat = MakeMaterial("HouseWall", new Color(0.96f, 0.9f, 0.8f));
        Material roofMat = MakeMaterial("HouseRoof", new Color(0.7f, 0.2f, 0.15f));
        Material doorMat = MakeMaterial("HouseDoor", new Color(0.45f, 0.25f, 0.12f));
        Material windowMat = MakeMaterial("HouseWindow", new Color(0.6f, 0.85f, 1f));
        Material chimneyMat = MakeMaterial("HouseChimney", new Color(0.6f, 0.35f, 0.3f));
        Material knobMat = MakeMaterial("HouseKnob", new Color(0.95f, 0.8f, 0.25f));

        var house = new GameObject("House");
        house.transform.position = position;
        Transform t = house.transform;

        // Walls keep their box collider so players can't walk through the house.
        // The door side faces -Z, towards the players' starting position.
        Box("Walls", t, new Vector3(0f, 1.5f, 0f), new Vector3(5f, 3f, 5f), wallMat, keepCollider: true);
        MeshPart("Roof", t, prism, new Vector3(0f, 2.95f, 0f), new Vector3(6.2f, 2.2f, 6.2f), Quaternion.identity, roofMat);
        Box("Chimney", t, new Vector3(1.6f, 4.5f, 0.8f), new Vector3(0.6f, 1.4f, 0.6f), chimneyMat);
        Box("Door", t, new Vector3(0f, 1f, -2.53f), new Vector3(1.2f, 2f, 0.1f), doorMat);
        Part("DoorKnob", t, new Vector3(0.4f, 1f, -2.62f), Vector3.one * 0.14f, knobMat);
        Box("WindowL", t, new Vector3(-1.6f, 1.8f, -2.53f), new Vector3(1f, 1f, 0.1f), windowMat);
        Box("WindowR", t, new Vector3(1.6f, 1.8f, -2.53f), new Vector3(1f, 1f, 0.1f), windowMat);

        Undo.RegisterCreatedObjectUndo(house, "Create House");
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

    static Transform MeshPart(string name, Transform parent, Mesh mesh, Vector3 localPos, Vector3 localScale, Quaternion localRot, Material mat)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = localScale;
        return go.transform;
    }

    static Material MakeMaterial(string name, Color color)
    {
        const string folder = "Assets/Materials";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "Materials");

        string path = $"{folder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Saves a generated mesh as an asset so prefabs and scenes can reference it.
    static Mesh SaveMesh(string path, Mesh mesh)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Meshes"))
            AssetDatabase.CreateFolder("Assets", "Meshes");

        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        existing.Clear();
        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    // Unit triangular prism (gable roof): base 1 wide in X at y=0, apex at y=1, extruded 1 along Z.
    static Mesh CreatePrismMesh()
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris = new List<int>();

        Vector3 a = new Vector3(-0.5f, 0f, -0.5f), a2 = new Vector3(-0.5f, 0f, 0.5f);
        Vector3 c = new Vector3(0.5f, 0f, -0.5f), c2 = new Vector3(0.5f, 0f, 0.5f);
        Vector3 b = new Vector3(0f, 1f, -0.5f), b2 = new Vector3(0f, 1f, 0.5f);

        void Tri(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            int i = verts.Count;
            verts.Add(p0); verts.Add(p1); verts.Add(p2);
            norms.Add(n); norms.Add(n); norms.Add(n);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        Tri(a, b, c);                    // front gable (-Z)
        Tri(a2, c2, b2);                 // back gable (+Z)
        Tri(a, c, c2); Tri(a, c2, a2);   // bottom
        Tri(a, a2, b2); Tri(a, b2, b);   // left slope
        Tri(c, b2, c2); Tri(c, b, b2);   // right slope

        var mesh = new Mesh { name = "Prism" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
