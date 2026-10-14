using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;
using static UnityEngine.Mathf;
using static SkinChanger.SkinChanger;

namespace SkinChanger.Emote_System;

public class Emote
{
	// Honestly this is probably too much but eh, flexibility is good
	public class AudioGroup(List<AudioClip> clips, float delay, bool loopAudio = false)
	{
		// List of clips to randomly choose from
		public List<AudioClip> Clips = clips;
		public float Delay = delay;
		
		public bool loopAudio = loopAudio;
	}

	private readonly Animator animator;
	
	private readonly Action<EmoteController> onTrigger;
	private readonly Action<EmoteController> animUpdate;
	private readonly Action<EmoteController> onStop;

	private readonly OWAudioSource audioSource;
	
	private readonly bool fadeAudio;
	private readonly float fadeDuration;

	// Allows emotes to play sequential audio effects
	// though each one can also pick from a random selection
	private readonly List<AudioGroup> audioGroups;
	
	private readonly ParticleSystem particleSystem;

	private int? triggerHash;
	private int? holdHash;

	public string EmoteTrigger { get; private set; }
	
	public bool IsActive { get; private set; }

	public bool HasAnimation => triggerHash.HasValue && animator != null && HasTrigger(triggerHash.Value);
	public bool NeedsRelease => HasAnimation || audioGroups?.Any(x => x.loopAudio) == true;
	public bool HasSound => audioSource != null && audioGroups is { Count: > 0 };
	public bool HasParticles => particleSystem != null;

	public Emote(
		Animator animator,
		string triggerName,
		string holdParameter = null,
		Action<EmoteController> onTrigger = null,
		Action<EmoteController> animUpdate = null,
		Action<EmoteController> onStop = null,
		OWAudioSource audioSource = null,
		List<AudioGroup> audioGroups = null,
		bool fadeAudio = false,
		float fadeDuration = 1.0f,
		ParticleSystem particleSystem = null
	)
	{
		this.animator = animator;
		this.onTrigger = onTrigger;
		this.animUpdate = animUpdate;
		this.onStop = onStop;
		this.audioSource = audioSource;
		this.fadeAudio = fadeAudio;
		this.fadeDuration = fadeDuration;
		this.audioGroups = audioGroups;
		this.particleSystem = particleSystem;

		EmoteTrigger = triggerName;
		triggerHash = Animator.StringToHash(triggerName);
		
		if (holdParameter != null)
			holdHash = Animator.StringToHash(holdParameter);
	}

	public bool Trigger(EmoteController instance)
	{
		// Prevents double triggers
		if (HasAnimation && instance.IsEmoting)
			return false;

		if (HasAnimation)
		{
			if (holdHash.HasValue)
				animator.SetBool(holdHash.Value, true);

			animator.SetTrigger(triggerHash.Value);

			instance.IsEmoting = true;
			IsActive = true;
		}
		
		onTrigger?.Invoke(instance);

		if (HasSound)
			PlayAudioSequence(instance);

		if (HasParticles)
			particleSystem.Play();

		return true;
	}

	private void PlayAudioSequence(EmoteController instance)
	{
		// An outside IEnumerator is necessary because we don't
		// want them to play in sequence. Delay is how far
		// into the emote it should play, not after the previous
		IEnumerator PlayAudioGroup(AudioGroup group)
		{
			if (group.Delay > 0f)
				yield return new WaitForSeconds(group.Delay);
			
			var clip = group.Clips[Random.Range(0, group.Clips.Count)];
			
			// Small bug that makes it so emotes that play a sound
			// while previously doing an emote that fades out
			// makes it not play the sound correctly
			if (fadeAudio)
				audioSource.FadeIn(fadeDuration, true);
			else
				audioSource.SetLocalVolume(1f);
			
			if (group.loopAudio)
			{
				audioSource.clip = clip;
				audioSource.loop = true;
				audioSource.Play();
			}
			else
			{
				audioSource.PlayOneShot(clip);
			}
		}
		
		foreach (var group in audioGroups)
			instance.StartCoroutine(PlayAudioGroup(group));
	}

	public void Release()
	{
		if (!IsActive)
			return;
		
		if (holdHash.HasValue)
			animator.SetBool(holdHash.Value, false);

		if (HasSound && audioSource.loop)
		{
			audioSource.loop = false;

			if (fadeAudio)
			{
				audioSource.FadeOut(fadeDuration);
			}
			else
			{
				audioSource.Stop();
				audioSource.clip = null;
			}
		}
	}

	public void Stop(EmoteController instance)
	{
		IsActive = false;

		if (HasAnimation)
			instance.IsEmoting = false;

		if (HasParticles)
			particleSystem.Stop();
		
		onStop?.Invoke(instance);
	}

	public void Update(EmoteController instance)
	{
		if (IsActive)
			animUpdate?.Invoke(instance);
	}

	bool HasTrigger(int hash)
	{
		foreach (var parameter in animator.parameters)
		{
			if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.nameHash == hash)
				return true;
		}

		return false;
	}
}

public class TravellersController(OWAudioSource audioSource)
{
	public OWAudioSource audioSource = audioSource;

	// This is not the same for every traveler song
	// but its a few dozen millisecond difference at most
	private const float TRAVELER_LOOP_TIME = 41.741f;

	private int lastLoop = -1;

	// This isn't synced for all players but it's synced locally
	// Could share current time with new players if wanted
	private float GetTravellersTime()
	{
		return Repeat(Time.timeSinceLevelLoad, TRAVELER_LOOP_TIME);
	}

	private int GetTravellersLoop()
	{
		return FloorToInt(Time.timeSinceLevelLoad / TRAVELER_LOOP_TIME);
	}
	
	// For resyncing
	// Doesn't always line up, especially if corresponding
	// emote is played near the end (don't know why though)
	public void Update()
	{
		int currentLoop = GetTravellersLoop();

		if (currentLoop != lastLoop)
		{
			lastLoop = currentLoop;

			if (audioSource.isPlaying)
				audioSource.time = 0f;
		}
	}

	public void StartTravelerMusic(AudioClip clip)
	{
		if (clip == null)
			return;

		audioSource.clip = clip;
		audioSource.loop = true;

		audioSource.time = Min(
			GetTravellersTime(),
			clip.length - 0.001f /* just in case */
		);
		
		lastLoop = GetTravellersLoop();

		audioSource.SetLocalVolume(0f);
		audioSource.Play();
		audioSource.FadeIn(1f);
	}

	public void StopTravelerMusic()
	{
		// Doesn't stop playing so it lines up even when
		// starting to play again, though probably unnecessary
		// due to the syncing when starting the music
		audioSource.FadeOut(1f);
	}
}

public class EmoteController : MonoBehaviour
{
	public enum EmoteType
	{
		Inhabitant_Prisoner_PlayInstrument,
		Inhabitant_Death,
		Inhabitant_Kill,
		Inhabitant_Scream,
		Inhabitant_Grunt_Sound,
		Inhabitant_Investigate,
		Inhabitant_Shine_Light,
		Inhabitant_Begin_Stalk,
		Inhabitant_Howl,
		Inhabitant_Prisoner_Howl,
        
		Nomai_PlayingMusic,
		Nomai_Playing,
		Nomai_ListenToPlayer,
		Nomai_WritingResponse,
		Nomai_CreateWordStones,
		Nomai_GestureToWordStones,
		Nomai_CreateCairns,
		Nomai_GestureToCairns,
        
		Riebeck_PlayingMusic,
		Riebeck_SittingIdle,
		Riebeck_Talking,
		
		Chert_PlayInstrument,
		Chert_Sit,
		Chert_ChatterChipper,
		
		Gabbro_PlayInstrument,
		Gabbro_SittingIdle,
		Gabbro_Float,
		
		Feldspar_PlayInstrument,
		Feldspar_SittingIdle,
		Feldspar_Talking,
		
		Esker_PlayInstrument,
		Esker_SittingIdle
	}
	
	public Animator animator { get; private set; }
	public OWAudioSource audioSource { get; private set; }
	public ParticleSystem particleSystem { get; private set; }
	public TravellersController travellersController { get; private set; }
	
	private int EmoteLayer;
	
	public bool IsEmoting { get; internal set; }
	public Emote CurrentEmote { get; private set; }
	
	#region Inhabitant Specifics
	
	private static readonly int DeathFade = Animator.StringToHash("DeathFade");
	private static readonly int KillType = Animator.StringToHash("KillType");
	private static readonly int Inhabitant_DissolveProgress = Shader.PropertyToID("_DissolveProgress");

	public void UpdateDeathFade(float? fade = null)
	{
		float fadeC = animator.GetFloat(DeathFade);

		if (fade.HasValue)
			fadeC = fade.Value;

		foreach (var renderer in _renderers)
		{
			for (int i = 0; i < renderer.sharedMaterials.Length; i++)
			{
				renderer.GetPropertyBlock(_block, i);
				_block.SetFloat(Inhabitant_DissolveProgress, fadeC);
				renderer.SetPropertyBlock(_block, i);
			}
		}
	}
	
	#endregion
	
	// -------------------------------------------------

	private Dictionary<EmoteType, Emote> emotes = new();

	private static readonly int NoEmoteState = Animator.StringToHash("No Emote");
	private bool _emoteEnteredState;

	private static AssetBundle AudioBundle;

	private MaterialPropertyBlock _block;
	private Renderer[] _renderers;

	private RuntimeAnimatorController _lastController;
	
	// -------------------------------------------------

	// For networking
	// Static because networking finds this controller
	// from the playerID
	public static Action<EmoteType> onEmoteStart;
	public static Action onEmoteReleased;
	
	// -------------------------------------------------

	private void Awake()
	{
		animator = GetComponent<Animator>();
		audioSource = GetComponent<OWAudioSource>();

		// probably not the best way but eh
		var travellersAudioSource = transform.Find("Music")?.GetComponent<OWAudioSource>();
		travellersController = new TravellersController(travellersAudioSource);
		particleSystem = GetComponentInChildren<ParticleSystem>();

		_renderers = GetComponentsInChildren<Renderer>(true);
		_block = new MaterialPropertyBlock();

		RefreshAnimatorController();
		
		// -------------------------------------------------

		AudioBundle ??= instance.ModHelper.Assets.LoadBundle("NonPlayerAssets/audio");
		
		#region Inhabitants
        
        emotes[EmoteType.Inhabitant_Death] = new Emote(
            animator: animator,
            triggerName: "Death",
            "HoldDeath",
            null,
            controller => controller.UpdateDeathFade(),
            controller => controller.UpdateDeathFade(0f),
            audioSource,
            [
	            new([
					AudioBundle.LoadAsset<AudioClip>("Ghost Individual Death 3.ogg"), 
					AudioBundle.LoadAsset<AudioClip>("Ghost Individual Death 4.ogg")
	            ], 0.15f)
            ],
            particleSystem: particleSystem
        );

        emotes[EmoteType.Inhabitant_Kill] = new Emote(
            animator,
            "Grab",
            audioSource: audioSource,
            audioGroups: [
                new([
	                AudioBundle.LoadAsset<AudioClip>("Ghost Grab Player 1.ogg"),
	                AudioBundle.LoadAsset<AudioClip>("Ghost Grab Player 2.ogg")
                ], 0.5f),
                new([AudioBundle.LoadAsset<AudioClip>("Ghost Blow Out Lantern Charge")], 4f),
                new([AudioBundle.LoadAsset<AudioClip>("Ghost Blow Out Lantern")], 5.4f)
            ]
        );

        emotes[EmoteType.Inhabitant_Scream] = new Emote(
            animator,
            "SeePlayer",
            audioSource: audioSource,
            audioGroups: [
                new([
	                AudioBundle.LoadAsset<AudioClip>("Ghost Shout 4.ogg"), 
	                AudioBundle.LoadAsset<AudioClip>("Ghost Shout 6.ogg")
                ], 0.1f),
            ]
        );
        
        emotes[EmoteType.Inhabitant_Grunt_Sound] = new Emote(
            null,
            null,
            audioSource: audioSource,
            audioGroups: [
                new([
	                AudioBundle.LoadAsset<AudioClip>("Ghost Grunt 1.ogg"), 
	                AudioBundle.LoadAsset<AudioClip>("Ghost Grunt 2_smoothfade.ogg"),
	                AudioBundle.LoadAsset<AudioClip>("Ghost Grunt 3.ogg"),
	                AudioBundle.LoadAsset<AudioClip>("Ghost Grunt 4.ogg"),
	                AudioBundle.LoadAsset<AudioClip>("Ghost Grunt 5.ogg"),
	                AudioBundle.LoadAsset<AudioClip>("Ghost Grunt 8.ogg"),
	                AudioBundle.LoadAsset<AudioClip>("Ghost Identify Fail.ogg") /* Similar enough to include here */
                ], 0f)
            ]
        );
        
        emotes[EmoteType.Inhabitant_Begin_Stalk] = new Emote(
            null,
            null,
            audioSource: audioSource,
            audioGroups: [
                new([
	                AudioBundle.LoadAsset<AudioClip>("Ghost Begin Stalk Grunt 1.ogg"), 
	                AudioBundle.LoadAsset<AudioClip>("Ghost Begin Stalk Grunt 2.ogg")
                ], 0f)
            ]
        );
        
        emotes[EmoteType.Inhabitant_Shine_Light] = new Emote(
            null,
            null,
            audioSource: audioSource,
            audioGroups: [new([AudioBundle.LoadAsset<AudioClip>("Ghost Shines Light.ogg")], 0f)]
        );
        
        emotes[EmoteType.Inhabitant_Howl] = new Emote(
            null,
            null,
            audioSource: audioSource,
            audioGroups: [
                new([
	                AudioBundle.LoadAsset<AudioClip>("Single Ghost Scream 1.ogg"), 
	                AudioBundle.LoadAsset<AudioClip>("Single Ghost Scream 14.ogg")
                ], 0f)
            ]
        );
        
        emotes[EmoteType.Inhabitant_Investigate] = new Emote(
            null,
            null,
            audioSource: audioSource,
            audioGroups: [
                new([
	                AudioBundle.LoadAsset<AudioClip>("Ghost Investiagation Grunt 2.ogg"), 
	                AudioBundle.LoadAsset<AudioClip>("Ghost Investigation Grunt.ogg")
                ], 0f)
            ]
        );
        
        emotes[EmoteType.Inhabitant_Prisoner_Howl] = new Emote(
            animator,
            "PrisonerHowl",
            audioSource: audioSource,
            audioGroups: [new([AudioBundle.LoadAsset<AudioClip>("Mournful Prisoner 3.ogg")], 0f)]
        );
        
        var prisonerClip = AudioBundle.LoadAsset<AudioClip>("OW_TravelerTheme_newtraveler 061021 AP.ogg");
        emotes[EmoteType.Inhabitant_Prisoner_PlayInstrument] = new Emote(
            animator,
            "PlayInstrument",
            "IsPlayingInstrument",
            onTrigger: controller => controller.travellersController.StartTravelerMusic(prisonerClip),
            onStop: controller => controller.travellersController.StopTravelerMusic()
        );
        
        #endregion
        
        #region Nomai

        emotes[EmoteType.Nomai_Playing] = new Emote(
            animator,
            "StartPlaying",
            "IsPlaying"
        );

        var pianoClip = AudioBundle.LoadAsset<AudioClip>("OW_TravelerTheme_piano.ogg");
        emotes[EmoteType.Nomai_PlayingMusic] = new Emote(
            animator,
            "StartPlaying",
            "IsPlaying",
            onTrigger: controller => controller.travellersController.StartTravelerMusic(pianoClip),
            onStop: controller => controller.travellersController.StopTravelerMusic()
        );

        emotes[EmoteType.Nomai_ListenToPlayer] = new Emote(
            animator,
            "ListenToPlayer",
            "IsListeningToPlayer"
        );

        emotes[EmoteType.Nomai_CreateCairns] = new Emote(
            animator,
            "CreateCairns"
        );
        
        emotes[EmoteType.Nomai_GestureToCairns] = new Emote(
            animator,
            "GestureToCairns"
        );
        
        emotes[EmoteType.Nomai_CreateWordStones] = new Emote(
            animator,
            "CreateWordStones"
        );
        
        emotes[EmoteType.Nomai_GestureToWordStones] = new Emote(
            animator,
            "GestureToWordStones"
        );

        emotes[EmoteType.Nomai_WritingResponse] = new Emote(
            animator,
            "WriteResponse",
            "IsWritingResponse"
        );

        #endregion

        #region Riebeck

        var banjoClip = AudioBundle.LoadAsset<AudioClip>("OW_TravelerTheme_banjo.ogg");
        emotes[EmoteType.Riebeck_PlayingMusic] = new Emote(
            animator,
            "StartPlay",
            "IsPlaying",
            onTrigger: controller => controller.travellersController.StartTravelerMusic(banjoClip),
            onStop: controller => controller.travellersController.StopTravelerMusic()
        );

        emotes[EmoteType.Riebeck_Talking] = new Emote(
            animator,
            "StartTalking",
            "IsTalking"
        );

        emotes[EmoteType.Riebeck_SittingIdle] = new Emote(
            animator,
            "SitIdle",
            "IsSittingIdle"
        );

        #endregion
        
		#region Chert

		var drumsClip = AudioBundle.LoadAsset<AudioClip>("OW_TravelerTheme_drums.ogg");

		emotes[EmoteType.Chert_PlayInstrument] = new Emote(
		    animator,
		    "PlayInstrument",
		    "IsPlayingInstrument",
		    onTrigger: controller => controller.travellersController.StartTravelerMusic(drumsClip),
		    onStop: controller => controller.travellersController.StopTravelerMusic()
		);

		emotes[EmoteType.Chert_Sit] = new Emote(
		    animator,
		    "Idle",
		    "IsIdling"
		);

		emotes[EmoteType.Chert_ChatterChipper] = new Emote(
		    animator,
		    "Talking",
		    "IsTalking"
		);

		#endregion

		#region Gabbro

		var fluteClip = AudioBundle.LoadAsset<AudioClip>("OW_TravelerTheme_flute.ogg");

		emotes[EmoteType.Gabbro_PlayInstrument] = new Emote(
		    animator,
		    "Flute",
		    "IsPlayingInstrument",
		    onTrigger: controller => controller.travellersController.StartTravelerMusic(fluteClip),
		    onStop: controller => controller.travellersController.StopTravelerMusic()
		);

		emotes[EmoteType.Gabbro_SittingIdle] = new Emote(
		    animator,
		    "Idle",
		    "IsIdling"
		);

		emotes[EmoteType.Gabbro_Float] = new Emote(
		    animator,
		    "Float",
		    "IsFloating"
		);

		#endregion

		#region Feldspar

		var harmonicaClip = AudioBundle.LoadAsset<AudioClip>("OW_TravelerTheme_harmonica.ogg");

		emotes[EmoteType.Feldspar_PlayInstrument] = new Emote(
		    animator,
		    "PlayHarmonica",
		    "IsPlayingInstrument",
		    onTrigger: controller => controller.travellersController.StartTravelerMusic(harmonicaClip),
		    onStop: controller => controller.travellersController.StopTravelerMusic()
		);

		emotes[EmoteType.Feldspar_SittingIdle] = new Emote(
		    animator,
		    "Idle",
		    "IsIdling"
		);

		emotes[EmoteType.Feldspar_Talking] = new Emote(
		    animator,
		    "Talking",
		    "IsTalking"
		);

		#endregion

		#region Esker

		var whistleClip = AudioBundle.LoadAsset<AudioClip>("OW_TravelerTheme_whistle.ogg");

		emotes[EmoteType.Esker_PlayInstrument] = new Emote(
		    animator,
		    "Idle",
		    "IsIdling",
		    onTrigger: controller => controller.travellersController.StartTravelerMusic(whistleClip),
		    onStop: controller => controller.travellersController.StopTravelerMusic()
		);

		emotes[EmoteType.Esker_SittingIdle] = new Emote(
		    animator,
		    "Idle",
		    "IsIdling"
		);

		#endregion
	}

	public void OnDisable()
	{
		CurrentEmote?.Release();
		StopCurrentEmote();

		if (audioSource != null)
		{
			audioSource.Stop();
			audioSource.loop = false;
			audioSource.clip = null;
		}

		var travellersAS = travellersController?.audioSource;
		if (travellersAS != null)
		{
			travellersAS.Stop();
			travellersAS.loop = false;
			travellersAS.clip = null;
		}

		IsEmoting = false;
		_emoteEnteredState = false;
	}

	// EmoteLayer gets set to -1 when the controller
	// changes for some reason
	private void RefreshAnimatorController()
	{
		_lastController = animator.runtimeAnimatorController;
		EmoteLayer = animator.GetLayerIndex("Emotes Layer");
	}

	public bool TryTriggerEmote(EmoteType type)
	{
		if (!emotes.TryGetValue(type, out var emote))
			return false;

		if (!emote.Trigger(this))
			return false;

		if (emote.NeedsRelease)
		{
			CurrentEmote = emote;
			_emoteEnteredState = false;
		}

		if (IsLocalPlayer())
			onEmoteStart?.Invoke(type);

		return true;
	}

	public void ReleaseCurrentEmote()
	{
		CurrentEmote?.Release();
		
		if (IsLocalPlayer())
			onEmoteReleased?.Invoke();
	}

	public void StopCurrentEmote()
	{
		if (CurrentEmote == null)
			return;

		CurrentEmote.Stop(this);
		CurrentEmote = null;
		
		_emoteEnteredState = false;
	}

	private bool IsLocalPlayer()
	{
		var playerBody = Locator.GetPlayerBody();
		return playerBody != null && transform.IsChildOf(playerBody.transform);
	}

	public void Update()
	{
		if (animator.runtimeAnimatorController != _lastController)
			RefreshAnimatorController();
		
		// Would run for all remote players if we didn't
		// making them all play the same animation lmao
		if (IsLocalPlayer())
		{
			// Will have to find a better input solution
			var kb = Keyboard.current;
			if (kb.gKey.isPressed)
			{
				var keys = new[]
				{
					kb.digit1Key,
					kb.digit2Key,
					kb.digit3Key,
					kb.digit4Key,
					kb.digit5Key,
					kb.digit6Key,
					kb.digit7Key,
					kb.digit8Key,
					kb.digit9Key,
					kb.digit0Key
				};

				(var startEmote, int emoteCount) = instance.CurrentCharacter.SettingName switch
				{
					"Inhabitant" => (EmoteType.Inhabitant_Prisoner_PlayInstrument, 10),
					"Nomai" => (EmoteType.Nomai_PlayingMusic, 8),
					"Riebeck" => (EmoteType.Riebeck_PlayingMusic, 3),
					_ => throw new ArgumentOutOfRangeException()
				};

				for (int i = 0; i < keys.Length && i < emoteCount; i++)
				{
					if (keys[i].wasPressedThisFrame)
					{
						var emote = (EmoteType)((int)startEmote + i);
						TryTriggerEmote(emote);
						break;
					}
				}
			}

			if (kb.gKey.wasReleasedThisFrame)
				ReleaseCurrentEmote();
		}
		
		// --------------------------------------
		
		var state = animator.GetCurrentAnimatorStateInfo(EmoteLayer);

		bool noEmote = state.shortNameHash == NoEmoteState;

		if (!noEmote)
			_emoteEnteredState = true;
		
		if (_emoteEnteredState && noEmote && !animator.IsInTransition(EmoteLayer))
			StopCurrentEmote();

		CurrentEmote?.Update(this);
		travellersController?.Update();
	}
}