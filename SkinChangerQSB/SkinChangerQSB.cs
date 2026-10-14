using OWML.Utils;
using QSB;
using QSB.Animation.Player;
using QSB.Player;
using QSB.Utility;
using QSB.WorldSync;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SkinChangerQSB;

/// <summary>
/// This was largely copied from https://github.com/xen-42/ow-qsb-skins but changing the method of applying skins to use SkinChanger
/// </summary>
public class SkinChangerQSB : MonoBehaviour
{
    public static SkinChangerQSB Instance { get; private set; }

    public string LocalSkin { get; private set; }

    // Links QSB playerID to current skin name and the game object they have on representing it
    private readonly Dictionary<uint, (string skinName, GameObject currentMesh)> _skins = new();

    public static string ChangeSkinMessage => nameof(ChangeSkinMessage);

    public void Start()
    {
        var harmony = new Harmony("pikpik_carrot.SkinChangerQSB");
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        QSBEmotesCompatibility.Init();
        QSBEmotesCompatibility.InitializeNetworking();
        
        SkinChanger.SkinChanger.instance.skinChanged += OnSkinChanged;

        Instance = this;

        LoadManager.OnCompleteSceneLoad += OnCompleteSceneLoad;
        QSBPlayerManager.OnAddPlayer += OnPlayerAdded;

        QSBHelper.API.RegisterHandler<string>(ChangeSkinMessage, OnReceiveChangeSkinMessage);
        QSBCore.RegisterNotRequiredForAllPlayers(SkinChanger.SkinChanger.instance);
    }

    private void OnCompleteSceneLoad(OWScene originalScene, OWScene loadScene)
    {
        if (loadScene == OWScene.SolarSystem || loadScene == OWScene.EyeOfTheUniverse)
        {
            // Wait for QSB to finish connecting and syncing first
            Delay.RunWhen(
                () => QSBWorldSync.AllObjectsReady,
                () => ChangePlayerSkin(QSBPlayerManager.LocalPlayer, LocalSkin)
            );
        }
    }

    public void OnSkinChanged(string skin)
    {
        LocalSkin = skin;
        if (QSBWorldSync.AllObjectsReady)
        {
            ChangePlayerSkin(QSBPlayerManager.LocalPlayer, skin);
        }
    }

    private void OnPlayerAdded(PlayerInfo player)
    {
        // Send them info about our skin
        // Make sure they've finished loading in first
        Delay.RunWhen(
            () => player.Body != null,
            () => SendChangeSkinMessage(LocalSkin, to: player.PlayerId)
        );
    }

    public void OnReceiveChangeSkinMessage(uint From, string Data)
    {
        Delay.RunWhen(
           () => QSBPlayerManager.GetPlayer(From).Body != null,
           () => Instance.ChangePlayerSkin(QSBPlayerManager.GetPlayer(From), Data)
        );
    }

    public static void SendChangeSkinMessage(string skin, uint to = uint.MaxValue)
    {
        QSBHelper.API.SendMessage(ChangeSkinMessage, skin, to: to);
    }

    public void ChangePlayerSkin(PlayerInfo player, string skinName)
    {
        DebugLogger.Write($"Changing skin on {player.PlayerId} to {skinName}");

        // Replace skin
        var isDefaultSkin = skinName == "Hatchling";

        if (player.IsLocalPlayer)
        {
            // Immediately tell all other clients to alter our skin
            SendChangeSkinMessage(skinName);

            // SkinChanger base will handle changing our skin for us, unless it's the hatchling where we have to insert our own jank
            SetHatchlingActive(player.Body, isDefaultSkin, false, PlayerState.IsWearingSuit());
        }
        else
        {
            // Backwards compat from the Inhabitant clothing customization update
            // Clothing choices are not synced: Could be a cool feature in the future
            // if (skinName == "Inhabitant")
            // {
            //     skinName = "Inhabitant_QSB";
            // }
            if (_skins.TryGetValue(player.PlayerId, out var skin))
            {
                if (skin.skinName == skinName)
                {
                    // Already has that skin
                    return;
                }
                else if (skin.currentMesh != null)
                {
                    GameObject.Destroy(skin.currentMesh);
                }
            }

            SetHatchlingActive(player.Body, isDefaultSkin, true, player.SuitedUp);

            if (isDefaultSkin)
            {
                _skins[player.PlayerId] = (skinName, null);
                player.Body.GetComponentInChildren<AnimatorMirror>()
                    .SetValue("_to", player.Body.transform.Find("REMOTE_Traveller_HEA_Player_v2").GetComponent<Animator>());

                // Make sure the helmet animator state is set properly
                var helmet = player.Body.GetComponentInChildren<HelmetAnimator>();
                helmet.enabled = true;
                // Can't find a QSB property that says if a helmet should be worn or not so just assume worn if in suit
                helmet.SetHelmetInstant(player.SuitedUp);
            }
            else
            {
                DebugLogger.Write("Creating new skin object");

                var skinChangerInstance = SkinChanger.SkinChanger.instance;
                var prefab = (skinChangerInstance.characters.FirstOrDefault(x => x.SettingName == skinName) ?? skinChangerInstance.MissingSkin).GameObject;
                var newPlayerModel = prefab.InstantiateInactive();

                // Fix layers (Nomai and Inhabitant heads are only visible to probe)
                foreach (var transform in newPlayerModel.GetComponentsInChildren<Transform>())
                {
                    if (transform.gameObject.layer == LayerMask.NameToLayer("VisibleToProbe"))
                    {
                        transform.gameObject.layer = LayerMask.NameToLayer("Default");
                    }
                }

                newPlayerModel.transform.parent = player.Body.transform;
                newPlayerModel.transform.localPosition = new Vector3(0, -1.03f, -0.2f);
                newPlayerModel.transform.localScale = Vector3.one * .1f;
                newPlayerModel.transform.localRotation = Quaternion.identity;
                Component.DestroyImmediate(newPlayerModel.GetComponent<PlayerAnimController>());
                player.Body.GetComponentInChildren<AnimatorMirror>()
                    .SetValue("_to", newPlayerModel.GetComponent<Animator>());
                newPlayerModel.SetActive(true);

                // Doesn't work on custom meshes and can cause a floating helmet to appear
                player.Body.GetComponentInChildren<HelmetAnimator>().enabled = false;

                _skins[player.PlayerId] = (skinName, newPlayerModel);
            }
        }
    }

    private Transform[] GetChildrenIncludingInactive(Transform parent)
    {
        List<Transform> children = new();
        for (int i = 0; i < parent.childCount; i++)
        {
            children.Add(parent.GetChild(i));
        }
        return children.ToArray();
    }

    private void SetHatchlingActive(GameObject playerBody, bool active, bool isRemote, bool isSuited)
    {
        var hatchlingBody = playerBody.transform.Find((isRemote ? "REMOTE_" : string.Empty) + "Traveller_HEA_Player_v2").gameObject;
        hatchlingBody.SetActive(true);

        if (active)
        {
            var playerMeshContainer = hatchlingBody.transform.Find("PlayerMeshContainer");
            if (playerMeshContainer == null)
            {
                playerMeshContainer = new GameObject("PlayerMeshContainer").transform;
                playerMeshContainer.transform.SetParent(hatchlingBody.transform, false);
                playerMeshContainer.gameObject.SetActive(false);
            }

            // Move all meshes from the inactive container to the regular object
            foreach (var child in GetChildrenIncludingInactive(playerMeshContainer))
            {
                child.SetParent(hatchlingBody.transform);
            }

            // Fix state of suit vs no suit
            hatchlingBody.transform.Find("player_mesh_noSuit:Traveller_HEA_Player").gameObject.SetActive(!isSuited);
            hatchlingBody.transform.Find("Traveller_Mesh_v01:Traveller_Geo").gameObject.SetActive(isSuited);
        }
        else
        {
            var playerMeshContainer = hatchlingBody.transform.Find("PlayerMeshContainer");
            if (playerMeshContainer == null)
            {
                playerMeshContainer = new GameObject("PlayerMeshContainer").transform;
                playerMeshContainer.transform.SetParent(hatchlingBody.transform, false);
                playerMeshContainer.gameObject.SetActive(false);
            }
            // Move all meshes from the inactive container to the regular object
            foreach (var child in GetChildrenIncludingInactive(hatchlingBody.transform))
            {
                if (child == playerMeshContainer) continue;
                child.transform.SetParent(playerMeshContainer);
            }
        }
    }
}