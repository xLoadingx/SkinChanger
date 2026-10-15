using UnityEngine;

namespace SkinChanger.Emote_System;

public class SkinLookIK : MonoBehaviour
{
	private Animator _animator;
	private Transform _camera;

	private static float _overallWeight = 0.35f;

	public void Awake()
	{
		_animator = GetComponent<Animator>();
	}

	private void OnAnimatorIK(int layerIndex)
	{
		_camera ??= _animator.transform.parent.GetComponentInChildren<OWCamera>().transform;

		_animator.SetLookAtPosition(_camera.position + _camera.forward * 10f);
		
		_animator.SetLookAtWeight(_overallWeight, 0.3f, 1f);
	}
}