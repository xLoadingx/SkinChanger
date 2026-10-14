using System.Linq;
using System.Reflection;
using HarmonyLib;
using QSB.Animation.Player;
using SkinChanger.Emote_System;
using UnityEngine;

namespace SkinChangerQSB;

public static class QSBEmotesCompatibility
{
	#region Messaging

	private const string StartEmoteMessage = "SkinChanger.StartEmote";
	private const string ReleaseEmoteMessage = "SkinChanger.ReleaseEmote";

	public static void InitializeNetworking()
	{
		if (QSBHelper.API == null)
			return;

		EmoteController.onEmoteStart += SendEmoteStart;
		EmoteController.onEmoteReleased += SendStopEmote;
		
		QSBHelper.API.RegisterHandler<int>(
			StartEmoteMessage, 
			OnReceiveStartEmote
		);
		
		QSBHelper.API.RegisterHandler<bool>(
			ReleaseEmoteMessage, 
			OnReceiveReleaseEmote
		);
	}

	public static void SendEmoteStart(EmoteController.EmoteType emote)
	{
		if (QSBHelper.API == null || !QSBHelper.API.GetIsInMultiplayer())
			return;
		
		QSBHelper.API.SendMessage(
			StartEmoteMessage,
			(int)emote
		);
	}

	public static void SendStopEmote()
	{
		if (QSBHelper.API == null || !QSBHelper.API.GetIsInMultiplayer())
			return;
		
		QSBHelper.API.SendMessage(
			ReleaseEmoteMessage,
			true
		);
	}

	private static void OnReceiveStartEmote(uint from, int emote)
	{
		var controller = GetRemoteEmoteController(from);

		if (controller == null)
		{
			// Could also just be from a skin without emotes but that's only
			// important if custom skins are registered (as all base skins have emotes)
			DebugLogger.WriteError($"Remote Emote Controller not found for player {from} on ReceiveStartEmote");
			return;
		}

		controller.TryTriggerEmote((EmoteController.EmoteType)emote);
	}

	// Payload has to be something
	private static void OnReceiveReleaseEmote(uint from, bool _)
	{
		var controller = GetRemoteEmoteController(from);

		if (controller == null)
		{
			DebugLogger.WriteError($"Remote Emote Controller not found for player {from} on ReceiveReleaseEmote");
			return;
		}

		controller.ReleaseCurrentEmote();
	}

	private static EmoteController GetRemoteEmoteController(uint playerID)
	{
		var body = QSBHelper.API?.GetPlayerBody(playerID);

		if (body == null)
		{
			DebugLogger.WriteError($"Player body for {playerID} not found");
			return null;
		}

		return body.GetComponentsInChildren<EmoteController>(true).FirstOrDefault();
	}
	
	#endregion
	
	#region AnimatorMirrorPatch
	
	private static FieldInfo _toField;

	private static MethodInfo _syncParams;
	private static MethodInfo _syncLayerWeights;
	private static MethodInfo _smoothFloats;

	public static void Init()
	{
		var type = typeof(AnimatorMirror);
		
		// Destination animator QSB mirrors to
		// (use this to detect the EmoteController)
		_toField = AccessTools.Field(type, "_to");

		// Original functions we still want to call
		_syncParams = AccessTools.Method(type, "SyncParams");
		_syncLayerWeights = AccessTools.Method(type, "SyncLayerWeights");
		_smoothFloats = AccessTools.Method(type, "SmoothFloats");
	}
	
	// Pretty icky but I can't think of a better way currently
	// Better than the original where the QSB dll wasn't referenced, however
	[HarmonyPatch(typeof(AnimatorMirror), nameof(AnimatorMirror.Update))]
	public static class AnimatorMirror_Update_Patch
	{
		public static bool Prefix(AnimatorMirror __instance)
		{
			if (_toField == null || _syncParams == null || _syncLayerWeights == null || _smoothFloats == null) return true;
			
			var to = (Animator)_toField.GetValue(__instance);
			
			// If it doesnt have an EmoteController then it's fine to replace
			if (to == null || to.GetComponent<EmoteController>() == null)
				return true;
			
			// Call original functions
			// CRUCIALLY: Skips runtime animator override to mirror controller
			_syncParams.Invoke(__instance, null);
			_syncLayerWeights.Invoke(__instance, null);
			_smoothFloats.Invoke(__instance, null);

			// and then prevent it from running the original (which overrides it)
			return false;
		}
	}
	
	#endregion
}