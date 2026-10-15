using OWML.Common;
using OWML.ModHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using OWML.Utils;
using Newtonsoft.Json.Linq;
using System.Linq;
using static SkinChanger.SkinChangerAPI;
using System.ComponentModel.Design;
using HarmonyLib;
using SkinChanger.Emote_System;

namespace SkinChanger
{
	public class SkinChanger : ModBehaviour
	{
		public static SkinChanger instance;

		// Action for QSB compat to listen to
		public Action<string> skinChanged;

		public PlayerCameraController PlayerCamera;
		// cant use array cuz one of them is a shape :((((
		public CapsuleCollider PlayerCollider1;
		public CapsuleCollider PlayerCollider2;
		public CapsuleShape PlayerCollider3;
		public GameObject Traveller_HEA_Player_v2;

		Dictionary<string, GameObject> prefabs = new();
		public List<PlayableCharacter> characters = new();

		public PlayableCharacter CurrentCharacter { get; private set; }
		public List<SkinChangerAPI.CustomSkin> CustomSkins = new();
		public bool Initialized { get; private set; }

		public PlayableCharacter MissingSkin { get; private set; }

		// variables for the inhabitant customization
		public SkinnedMeshRenderer ghostRenderer; // variable to store the inhabitant's skinned mesh renderer
		public GameObject[] ghostColors = new GameObject[11]; // variable to store all color configs for the skinned mesh renderer
		private string[] ghostColorNames = new string[] { "BLACK", "BLUE", "BROWN", "GREEN", "INVERSE", "RED", "RUGBLUE", "RUGGREEN", "RUGRED", "WHITE", "YELLOW" };
		public GameObject[] ghostAntlersL = new GameObject[6]; // array to store all inhabitant left antlers
		public GameObject[] ghostAntlersR = new GameObject[6]; // array to store all inhabitant right antlers
		public GameObject[] ghostAccessories = new GameObject[3]; // array to store all inhabitant accessories
		public Transform[] antlerTransforms = new Transform[2]; // array to store transform values of both antlers
		private bool hasInitialized; // checks if everything has been initialized
		private AssetBundle robeBundle; // to store the asset bundle for the robe reference

		public class PlayableCharacter
		{
			public GameObject GameObject;
			public string SettingName;
			public Vector3 CameraOffset;

			public float ColliderRadius;
			public float ColliderHeight;
			public Vector3 ColliderCenter;

			public PlayableCharacter(string prefabName, string settingName, Vector3 camOffset, float collRadius, float collHeight, Vector3 collCenter, bool hasEmotes = false)
			{
				GameObject = prefabName != null ? Instantiate(instance.prefabs[prefabName]) : instance.Traveller_HEA_Player_v2; // this line nres
				SettingName = settingName;
				CameraOffset = camOffset;

				ColliderRadius = collRadius;
				ColliderHeight = collHeight;
				ColliderCenter = collCenter;

				if (hasEmotes)
					GameObject.AddComponent<EmoteController>();
			}
		}

		public override object GetApi()
		{
			return new SkinChangerAPI();
		}

		// stolen from qsb and nh LOLOLLOLOLOLO
		public static void ReplaceShaders(GameObject prefab)
		{
			foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true)) // this line gives nre
			{
				foreach (var material in renderer.sharedMaterials)
				{
					if (material == null) continue;

					var replacementShader = Shader.Find(material.shader.name);
					if (replacementShader == null) continue;

					// preserve override tag and render queue (for Standard shader)
					// keywords and properties are already preserved
					if (material.renderQueue != material.shader.renderQueue)
					{
						var renderType = material.GetTag("RenderType", false);
						var renderQueue = material.renderQueue;
						material.shader = replacementShader;
						material.SetOverrideTag("RenderType", renderType);
						material.renderQueue = renderQueue;
					}
					else
					{
						material.shader = replacementShader;
					}
				}
			}
		}

		private void Start()
		{
			instance = this;

			// Enable QSB compat if required
			if (ModHelper.Interaction.ModExists("Raicuparta.QuantumSpaceBuddies"))
			{
				// QSB compat has to be in a different DLL since it depends on the QSB DLL.
				// Adapted from QSB-NH compat in Quantum Space Buddies
				var skinChangerQSB = Assembly.LoadFrom(Path.Combine(ModHelper.Manifest.ModFolderPath, "SkinChangerQSB.dll"));
				gameObject.AddComponent(skinChangerQSB.GetType("SkinChangerQSB.SkinChangerQSB", true));
			}

			// Enable CommonCameraUtil compat if required
			if (ModHelper.Interaction.ModExists("xen.CommonCameraUtility"))
			{
				gameObject.AddComponent<ThirdPersonCompatibility>();
			}

			foreach (var path in Directory.EnumerateFiles(Path.Combine(ModHelper.Manifest.ModFolderPath, "Assets")))
			{
				if (Path.GetExtension(path) == ".manifest") continue; // ignore the non bundle files
				if (path == "Assets") continue; // This shouldn't be in the release but if it snuck in ignore it
				if (path == "audio") continue; // Not a skin bundle, only used in the Emote Controller

				LoadPrefab(path);
			}

			LoadManager.OnCompleteSceneLoad += (scene, loadScene) =>
			{
				if (loadScene is not (OWScene.SolarSystem or OWScene.EyeOfTheUniverse)) return;

				var player = GameObject.Find("Player_Body");
				PlayerCamera = player.transform.Find("PlayerCamera").GetComponent<PlayerCameraController>();

				PlayerCollider1 = player.GetComponent<CapsuleCollider>();
				PlayerCollider2 = player.transform.Find("PlayerDetector").GetComponent<CapsuleCollider>();
				PlayerCollider3 = player.transform.Find("PlayerDetector").GetComponent<CapsuleShape>();

				Traveller_HEA_Player_v2 = player.transform.Find("Traveller_HEA_Player_v2").gameObject;

				MissingSkin = new PlayableCharacter("Traveller_HEA_Player_v0", "Error", new Vector3(0, 0.8496093f, 0.15f), 0.5f, 2f, Vector3.zero);

				// make the instances of the prefabs
				characters = new List<PlayableCharacter>()
				{
					//Unity Object name                                //Setting Name      //Camera Offset                     //Collider Radius, Height, Center                // Has Emotes?     
					new PlayableCharacter(null /*uses existing v2*/,   "Hatchling",        new Vector3(0, 0.8496093f, 0.15f),  0.5f, 2f,         Vector3.zero),
					new PlayableCharacter("Traveller_HEA_Player_v3",   "Inhabitant",       new Vector3(0, 2.5004f, 0.5701f),   0.5f, 3.6f,       new Vector3(0f, 0.875f, 0f),   true),
					new PlayableCharacter("Traveller_HEA_Player_v4",   "Nomai",            new Vector3(0, 1.1f, 0.3f),         0.5f, 2.5f,       new Vector3(0f, 0.3f, 0f),     true),
					new PlayableCharacter("Traveller_HEA_Player_v5",   "Chert",            new Vector3(0, 0.3f, 0.2f),         0.5f, 1.5f,       new Vector3(0f, -0.2f, 0f),    true),
					new PlayableCharacter("Traveller_HEA_Player_v6",   "Esker",            new Vector3(0, 0.9f, 0.2f),         0.5f, 2f,         Vector3.zero,                  true),
					new PlayableCharacter("Traveller_HEA_Player_v7",   "Riebeck",          new Vector3(0, 1.1f, 0.3f),         0.5f, 2.5f,       new Vector3(0f, 0.25f, 0),     true),
					new PlayableCharacter("Traveller_HEA_Player_v8",   "Gabbro",           new Vector3(0, 1.1f, 0.2f),         0.5f, 2.5f,       new Vector3(0f, 0.25f, 0),     true),
					new PlayableCharacter("Traveller_HEA_Player_v9",   "Feldspar",         new Vector3(0f, 0.7f, 0.2f),        0.5f, 2f,         Vector3.zero,                  true),
					new PlayableCharacter("Traveller_HEA_Player_v10",  "Slate",            new Vector3(0, 1.2f, 0.2f),         0.5f, 2.5f,       new Vector3(0f, 0.25f, 0)),
					new PlayableCharacter("Traveller_HEA_Player_v11",  "Hal",              new Vector3(0, 0.8496093f,          0.15f), 0.5f, 2f, Vector3.zero),
					new PlayableCharacter("Traveller_HEA_Player_v12",  "Hornfels",         new Vector3(0, 1.1f, 0.2f),         0.5f, 2.5f,       new Vector3(0f, 0.25f, 0)),
					new PlayableCharacter("Traveller_HEA_Player_v13",  "Gossan",           new Vector3(0f, 0.5f, 0.1f),        0.5f, 2f,         Vector3.zero),
					new PlayableCharacter("Traveller_HEA_Player_v14",  "Mica",             new Vector3(0, 0.3f, 0.1f),         0.5f, 1.5f,       new Vector3(0f, -0.2f, 0)),
					new PlayableCharacter("Traveller_HEA_Player_v15",  "Arkose",           new Vector3(0, 0.3f, 0.1f),         0.5f, 1.5f,       new Vector3(0f, -0.2f, 0)),
					new PlayableCharacter("Traveller_HEA_Player_v16",  "Tephra",           new Vector3(0, 0.3f, 0.1f),         0.5f, 1.5f,       new Vector3(0f, -0.2f, 0)),
					new PlayableCharacter("Traveller_HEA_Player_v17",  "Galena",           new Vector3(0, 0.3f, 0.1f),         0.5f, 1.5f,       new Vector3(0f, -0.2f, 0)),
					new PlayableCharacter("Traveller_HEA_Player_v18",  "Spinel",           new Vector3(0, 0.7f, 0.2f),         0.5f, 2f,         Vector3.zero),
					new PlayableCharacter("Traveller_HEA_Player_v19",  "Porphy",           new Vector3(0, 1.3f, 0.2f),         0.5f, 2.5f,       new Vector3(0f, 0.25f, 0)),
					new PlayableCharacter("Traveller_HEA_Player_v20",  "Rutile",           new Vector3(0, 0.8496093f, 0.15f),  0.5f, 2f,         Vector3.zero),
					new PlayableCharacter("Traveller_HEA_Player_v21",  "Marl",             new Vector3(0, 1.3f, 0.25f),        0.5f, 2.5f,       new Vector3(0f, 0.25f, 0)),
					new PlayableCharacter("Traveller_HEA_Player_v22",  "Gneiss",           new Vector3(0f, 0.8f, 0.2f),        0.5f, 2f,         Vector3.zero),
					new PlayableCharacter("Traveller_HEA_Player_v23",  "Moraine",          new Vector3(0, 0.3f, 0.1f),         0.5f, 1.5f,       new Vector3(0f, -0.2f, 0)),
					new PlayableCharacter("Traveller_HEA_Player_v24",  "Tuff",             new Vector3(0, 0.8496093f,          0.15f), 0.5f, 2f, Vector3.zero),
					new PlayableCharacter("Traveller_HEA_Player_v25",  "Tektite",          new Vector3(0, 1.2f, 0.2f),         0.5f, 2.5f,       new Vector3(0f, 0.25f, 0)),
					// The Inhabitant customization patch made breaking changes to QSB syncing. Because of this we use the old inhabitant model for QSB back compat
					new PlayableCharacter("Traveller_HEA_Player_v3_QSB","Inhabitant_QSB",   new Vector3(0, 2.2f, 0.27f),        0.5f, 3.6f,       new Vector3(0f, 0.875f, 0f)),
					MissingSkin
				};

				foreach (var customSkin in CustomSkins)
				{
					// Check if it loaded properly
					if (prefabs.ContainsKey(customSkin.PrefabID))
					{
						characters.Add(new PlayableCharacter(customSkin.PrefabID, customSkin.Name, customSkin.CameraOffset, customSkin.ColliderRadius, customSkin.ColliderHeight, customSkin.ColliderCenter));
					}
					else
					{
						// Default to an error sign, because thats funny!
						characters.Add(new PlayableCharacter("Traveller_HEA_Player_v0", customSkin.Name, new Vector3(0, 0.8496093f, 0.15f), 0.5f, 2f, Vector3.zero));
					}
				}

				// parent them to the player
				foreach (var character in characters)
				{
					character.GameObject.transform.SetParent(player.transform, false);
					// this terrible position comes from vanilla player. cry about it
					character.GameObject.transform.localPosition = new Vector3(0, -1.03f, -0.2f);
					character.GameObject.transform.localScale = Vector3.one * .1f;

					if (character.SettingName is "Inhabitant" or "Nomai")
						character.GameObject.AddComponent<SkinLookIK>();
					
					if (character.SettingName != "Hatchling")
						character.GameObject.AddComponent<SkinCameraUpdate>();
				}

				ChangeSkin();
			};

			ModHelper.Events.Unity.FireOnNextUpdate(InitializeCustomSkins);
		}

		private void InitializeCustomSkins()
		{
			var modelSettings = ((ModHelper.Config as ModConfig).Settings["Model"] as JObject);
			var optionsProperty = modelSettings.Properties().First(x => x.Name == "options");
			var optionsList = (optionsProperty.Value as JArray).ToArray().Select(x => x.ToString()).ToList();

			var chosenOptionProperty = modelSettings.Properties().First(x => x.Name == "value");
			var chosenOption = (chosenOptionProperty.Value as JValue).ToString();

			if (EntitlementsManager.IsDlcOwned() == EntitlementsManager.AsyncOwnershipStatus.NotOwned)
			{
				optionsList.Remove("Inhabitant");
			}

			// Load all custom skins
			foreach (var customSkin in CustomSkins)
			{
				try
				{
					LoadPrefab(customSkin.BundlePath);
					optionsList.Add(customSkin.Name);
				}
				catch (Exception ex)
				{
					ModHelper.Console.WriteLine($"Failed to load custom skin {customSkin.Name} - {ex}");
				}
			}

			// Make sure only valid names are in the mod options list now
			optionsProperty.Value = JArray.FromObject(optionsList.ToArray());
			if (!optionsList.Contains(chosenOption))
			{
				chosenOptionProperty.Value = JValue.FromObject(optionsList.First());
			}

			Initialized = true;
			ModHelper.Console.WriteLine($"Done initializing {CustomSkins.Count} custom skins");
		}

		private void LoadPrefab(string path)
		{
			ModHelper.Console.WriteLine($"loading async {path}");
			var req1 = AssetBundle.LoadFromFileAsync(path);
			req1.completed += _ =>
			{
				var req2 = req1.assetBundle.LoadAllAssetsAsync<GameObject>();
				req2.completed += _ =>
				{
					var prefab = (GameObject)req2.asset;
					ReplaceShaders(prefab);
					var name = prefab.name;
					// The QSB duplicated of the Inhabitant has the same prefab name so we have to manually change the name here
					if (path.Contains("_qsb"))
					{
						name = name + "_QSB";
					}
					prefabs.Add(name, prefab);
					ModHelper.Console.WriteLine($"loaded prefab {prefab}");
				};
			};
		}

		public override void Configure(IModConfig config)
		{
			if (LoadManager.s_currentScene is not (OWScene.SolarSystem or OWScene.EyeOfTheUniverse)) return;
			ChangeSkin();
		}

		public const string ModelSetting = "Model";
		public const string ChangeCamera = "Change Camera";
		public const string NormalCameraSizeInShip = "Default Camera in Ship";
		public const string ChangeCollider = "Change Collider";

		// inhabitant setting strings
		public const string InhabitantClothing = "Inhabitant Clothing";
		public const string InhabitantLeftAntler = "Inhabitant Left Antler";
		public const string InhabitantRightAntler = "Inhabitant Right Antler";
		public const string InhabitantNecklace = "Inhabitant Necklace";
		public const string InhabitantLeftBracelet = "Inhabitant Left Bracelet";
		public const string InhabitantRightBracelet = "Inhabitant Right Bracelet";

		void Update()
		{
			
			var changeCamInShip = ModHelper.Config.GetSettingsValue<bool>(NormalCameraSizeInShip);
			if (changeCamInShip)
			{
				if (LoadManager.GetCurrentScene() == OWScene.SolarSystem || LoadManager.GetCurrentScene() == OWScene.EyeOfTheUniverse)
				{
					if (Locator.GetPlayerSectorDetector().IsWithinSector(Sector.Name.Ship))
					{
						PlayerCamera._origLocalPosition = characters[0].CameraOffset;
					}
				}
			}
		}

		void ChangeSkin()
		{
			var modelSetting = ModHelper.Config.GetSettingsValue<string>(ModelSetting);
			var useCamera = ModHelper.Config.GetSettingsValue<bool>(ChangeCamera);
			var useCollider = ModHelper.Config.GetSettingsValue<bool>(ChangeCollider);

			// inhabitant settings
			var ghostClothes = ModHelper.Config.GetSettingsValue<string>(InhabitantClothing);
			var ghostAntlerL = ModHelper.Config.GetSettingsValue<string>(InhabitantLeftAntler);
			var ghostAntlerR = ModHelper.Config.GetSettingsValue<string>(InhabitantRightAntler);
			var ghostNecklace = ModHelper.Config.GetSettingsValue<bool>(InhabitantNecklace);
			var ghostBraceletL = ModHelper.Config.GetSettingsValue<bool>(InhabitantLeftBracelet);
			var ghostBraceletR = ModHelper.Config.GetSettingsValue<bool>(InhabitantRightBracelet);

			if (!useCollider)
			{
				PlayerCollider1.radius = characters[0].ColliderRadius;
				PlayerCollider2.radius = characters[0].ColliderRadius;
				PlayerCollider3.radius = characters[0].ColliderRadius;
				PlayerCollider1.height = characters[0].ColliderHeight;
				PlayerCollider2.height = characters[0].ColliderHeight;
				PlayerCollider3.height = characters[0].ColliderHeight;
				PlayerCollider1.center = characters[0].ColliderCenter;
				PlayerCollider2.center = characters[0].ColliderCenter;
				PlayerCollider3.center = characters[0].ColliderCenter;
			}

			foreach (var character in characters)
			{
				if (modelSetting == character.SettingName)
				{
					CurrentCharacter = character;

					character.GameObject.SetActive(true);

					SkinCameraUpdate skinCameraUpdate;
					if ((skinCameraUpdate = character.GameObject.GetComponent<SkinCameraUpdate>()) != null 
					    && character.SettingName != "Inhabitant"
					    && character.SettingName != "Nomai"
					)
						skinCameraUpdate.originalOffset = character.CameraOffset;

					if (useCollider)
					{
						PlayerCollider1.radius = character.ColliderRadius;
						PlayerCollider2.radius = character.ColliderRadius;
						PlayerCollider3.radius = character.ColliderRadius;
						PlayerCollider1.height = character.ColliderHeight;
						PlayerCollider2.height = character.ColliderHeight;
						PlayerCollider3.height = character.ColliderHeight;
						PlayerCollider1.center = character.ColliderCenter;
						PlayerCollider2.center = character.ColliderCenter;
						PlayerCollider3.center = character.ColliderCenter;
					}

					// TODO:
					// Fix this customization in a specific commit
					// Add actual instruments / props to Nomai and Inhabitant (include Artifact in customization OR make it hold an artifact the correct way)
					// Add the rest of the Hearthians (pain-maxxing)
					
					// // inhabitant customization
					// if (modelSetting == "Inhabitant")
					// {
					// 	// first-time setup for ghostbird customization, only runs once in the loop
					// 	if (!hasInitialized)
					// 	{
					// 		// gets all ghost robe colors
					// 		for (int i = 0; i < 11; i++)
					// 		{
					// 			ghostColors[i] = GameObject.Find("Player_Body/Traveller_HEA_Player_v3(Clone)/" + ghostColorNames[i] + "/player_mesh_noSuit:Traveller_HEA_Player/Ghostbird_Skin_01:Ghostbird_v004:Ghostbird_IP/Ghostbird_Skin_01:Ghostbird_v004:Ghostbird_Merged");
					// 		}
					// 	}
					//
					// 	// ghost robe color setup
					// 	for (int i = 0; i < ghostColorNames.Length; i++) // loops through all colors (total of 11)
					// 	{
					// 		if (ghostClothes == ghostColorNames[i]) // checks if the current color
					// 		{
					// 			ghostColors[i].SetActive(true); // sets clothing color
					// 			if (ghostColorNames[i] == "BLACK") // checks if you are at beginning of color array
					// 			{
					// 				ReorderInhabitantPaths(ghostColorNames[ghostColorNames.Length-1]); // temporarily sets paths to the last color in the array to disable its accessories and antlers
					// 			} else
					// 			{
					// 				ReorderInhabitantPaths(ghostColorNames[i - 1]); // temporarily sets paths to the previous color to disable its accessories and antlers
					// 			}
					// 			// disable previous color antlers
					// 			for (int j = 0; j < 6; j++)
					// 			{
					// 				if (ghostAntlersL[j] != null && ghostAntlersR[j] != null) // only run if the left and right antler isn't null
					// 				{
					// 					ghostAntlersL[j].SetActive(false); // sets each left antler to false
					// 					ghostAntlersR[j].SetActive(false); // sets each right antler to false
					// 				}
					// 			}
					// 			// disable previous color accessories
					// 			for (int j = 0; j < 3; j++)
					// 			{
					// 				if (ghostAccessories[j] != null) // only runs when the accessory isn't null
					// 				{
					// 					ghostAccessories[j].SetActive(false); // sets each accessory to false
					// 				}
					// 			}
					// 			ReorderInhabitantPaths(ghostClothes); // sets inhabitant antler and accessory path back to the correct color
					// 		}
					// 		else
					// 		{
					// 			ghostColors[i].SetActive(false); // deactivates all other colors that are inactive
					// 		}
					// 	}
					//
					// 	// antlers setup (loops through all 6 antlers)
					// 	for (int i = 0; i < 6; i++)
					// 	{
					// 		// left antler setup
					// 		if (i == ((int)ghostAntlerL[0] - '0') - 1) // gets the number at the first char of the current antler string
					// 		{
					// 			ghostAntlersL[i].SetActive(true); // if number matches the chosen setting, sets the gameobject active.
					// 		}
					// 		else
					// 		{
					// 			ghostAntlersL[i].SetActive(false); // otherwise set it to false.
					// 		}
					//
					// 		// right antler setup
					// 		if (i == ((int)ghostAntlerR[0] - '0') - 1)  // gets the number at the first char of the current antler string
					// 		{
					// 			ghostAntlersR[i].SetActive(true); // if number matches the chosen setting, sets the gameobject active.
					// 		}
					// 		else
					// 		{
					// 			ghostAntlersR[i].SetActive(false); // otherwise set it to false.
					// 		}
					// 	}
					//
					// 	// accessories setup
					// 	// ghost left bracelet setup
					// 	if (ghostBraceletL)
					// 	{
					// 		ghostAccessories[0].SetActive(true); // if the setting is set to true, enables the accessory.
					// 	}
					// 	else
					// 	{
					// 		ghostAccessories[0].SetActive(false); // otherwise sets it to false.
					// 	}
					//
					// 	// ghost right bracelet setup
					// 	if (ghostBraceletR)
					// 	{
					// 		ghostAccessories[1].SetActive(true); // if the setting is set to true, enables the accessory.
					// 	}
					// 	else
					// 	{
					// 		ghostAccessories[1].SetActive(false); // otherwise sets it to false.
					// 	}
					//
					// 	// ghost necklace setup
					// 	if (ghostNecklace)
					// 	{
					// 		ghostAccessories[2].SetActive(true); // if the setting is set to true, enables the accessory.
					// 	}
					// 	else
					// 	{
					// 		ghostAccessories[2].SetActive(false); // otherwise sets it to false.
					// 	}
					// }
				}
				else
				{
					character.GameObject.SetActive(false);
				}
			}

			skinChanged?.Invoke(modelSetting);
		}

		// changes the path of the antlers and accessories to the specified color passed into the method
		private void ReorderInhabitantPaths(string currentColor)
		{
			// get the accessories
			// index 0: left bracelet
			ghostAccessories[0] = GameObject.Find("Player_Body/Traveller_HEA_Player_v3(Clone)/" + currentColor + "/player_mesh_noSuit:Traveller_HEA_Player/Ghostbird_Skin_01:Ghostbird_v004:Ghostbird_IP/Ghostbird_Skin_01:Ghostbird_v004:Ghostbird_Accessories/Ghostbird_Skin_01:Ghostbird_v004:Bracelet_Left");
			// index 1: right bracelet
			ghostAccessories[1] = GameObject.Find("Player_Body/Traveller_HEA_Player_v3(Clone)/" + currentColor + "/player_mesh_noSuit:Traveller_HEA_Player/Ghostbird_Skin_01:Ghostbird_v004:Ghostbird_IP/Ghostbird_Skin_01:Ghostbird_v004:Ghostbird_Accessories/Ghostbird_Skin_01:Ghostbird_v004:Bracelet_Right");
			// index 2: necklace
			ghostAccessories[2] = GameObject.Find("Player_Body/Traveller_HEA_Player_v3(Clone)/" + currentColor + "/player_mesh_noSuit:Traveller_HEA_Player/Ghostbird_Skin_01:Ghostbird_v004:Ghostbird_IP/Ghostbird_Skin_01:Ghostbird_v004:Ghostbird_Accessories/Ghostbird_Skin_01:Ghostbird_v004:Necklace");

			// get antlers
			//index 0: left antler transform
			antlerTransforms[0] = GameObject.Find("Player_Body/Traveller_HEA_Player_v3(Clone)/" + currentColor + "/player_mesh_noSuit:Traveller_HEA_Player/Ghostbird_Skin_01:Ghostbird_v004:Ghostbird_IP/Ghostbird_Skin_01:Ghostbird_v004:Ghostbird_Accessories/Ghostbird_Skin_01:Ghostbird_v004:Antlers_Left").transform;
			//index 1: right antler transform
			antlerTransforms[1] = GameObject.Find("Player_Body/Traveller_HEA_Player_v3(Clone)/" + currentColor + "/player_mesh_noSuit:Traveller_HEA_Player/Ghostbird_Skin_01:Ghostbird_v004:Ghostbird_IP/Ghostbird_Skin_01:Ghostbird_v004:Ghostbird_Accessories/Ghostbird_Skin_01:Ghostbird_v004:Antlers_Right").transform;

			// gets the antlers
			for (int i = 0; i < 6; i++)
			{
				ghostAntlersL[i] = antlerTransforms[0].GetChild(i).gameObject; // gets all the left antlers
				ghostAntlersR[i] = antlerTransforms[1].GetChild(i).gameObject; // gets all the right antlers
			}
		}
	}
}
