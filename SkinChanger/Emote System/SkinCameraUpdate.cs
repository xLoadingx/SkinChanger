using System;
using UnityEngine;

namespace SkinChanger.Emote_System;

public class SkinCameraUpdate : MonoBehaviour
{
	private Animator _animator;
	public Vector3? _restingHeadPosition;
	public Vector3? originalOffset;
	private EmoteController _emoteController;

	public void Awake()
	{
		_animator = GetComponent<Animator>();
		_emoteController = GetComponent<EmoteController>();
	}

	private void LateUpdate()
	{
		// Not sure why the remote skins have this component
		// but this prevents weird camera bugs just in case
		if (!transform.IsLocalPlayer())
			return;

		if (!SkinChanger.instance.ModHelper.Config.GetSettingsValue<bool>(SkinChanger.ChangeCamera))
			return;

		var player = Locator.GetPlayerBody()?.transform;
		var head = _animator?.GetBoneTransform(HumanBodyBones.Head);

		if (player == null || head == null)
			return;

		var headPosition = player.InverseTransformPoint(head.position);

		if (!_emoteController.IsEmoting)
			_restingHeadPosition = headPosition;

		if (originalOffset.HasValue)
		{
			SkinChanger.instance.PlayerCamera._origLocalPosition =
				originalOffset.Value + (headPosition - _restingHeadPosition.Value);
		}
		else
		{
			// Nomai and Inhabitant
			SkinChanger.instance.PlayerCamera._origLocalPosition =
				headPosition + new Vector3(0f, 0.1f, 0.2f);
		}
	}
}