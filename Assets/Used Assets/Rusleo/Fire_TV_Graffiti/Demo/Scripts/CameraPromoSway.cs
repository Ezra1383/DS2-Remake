using UnityEngine;

namespace Rusleo.Promo
{
    public sealed class CameraPromoSway : MonoBehaviour
    {
        [Header("Position Sway")]
        [SerializeField] private float positionAmplitude = 0.05f;
        [SerializeField] private float positionFrequency = 0.5f;

        [Header("Rotation Sway")]
        [SerializeField] private float rotationAmplitude = 1.5f;
        [SerializeField] private float rotationFrequency = 0.4f;

        [Header("Smoothness")]
        [SerializeField] private float smooth = 5f;
        [SerializeField] private bool useUnscaledTime = true;

        private Vector3 _initialPosition;
        private Quaternion _initialRotation;
        private float _time;

        private void Awake()
        {
            _initialPosition = transform.localPosition;
            _initialRotation = transform.localRotation;
        }

        private void LateUpdate()
        {
            var dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            _time += dt;

            var posOffset = new Vector3(
                Mathf.Sin(_time * positionFrequency) * positionAmplitude,
                Mathf.Cos(_time * positionFrequency * 0.7f) * positionAmplitude * 0.5f,
                0f
            );

            var rotOffset = Quaternion.Euler(
                Mathf.Sin(_time * rotationFrequency) * rotationAmplitude,
                Mathf.Cos(_time * rotationFrequency * 0.8f) * rotationAmplitude,
                Mathf.Sin(_time * rotationFrequency * 0.6f) * rotationAmplitude * 0.5f
            );

            var targetPosition = _initialPosition + posOffset;
            var targetRotation = _initialRotation * rotOffset;

            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, dt * smooth);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, dt * smooth);
        }
    }
}