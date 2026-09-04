using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace CombatGirlsCharacterPack
{
    public class CameraController_AnimationReset : MonoBehaviour
    {
        [Header("Camera Set")]
        public Transform target;
        public float distance = 5f;
        public float heightOffset = 1f;

        [Header("Rotation Sensitivity")]
        public float xSensitivity = 15f;
        public float ySensitivity = 7f;
        public float rotationSpeedMultiplier = 2f;
        public float rotationDecay = 7.5f;

        [Header("Zoom & Y-Up")]
        public float zoomSpeed = 60f;
        public float yAdjustmentSpeed = 0.35f;

        private float initialDistance;
        private float initialHeightOffset;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Vector3 initialTargetPosition;
        private Animator targetAnimator;

        private float currentX = 0f;
        private float currentY = 0f;

        private Vector2 previousMousePosition;
        private float velocityX = 0f;
        private float velocityY = 0f;

        private bool isDragging = false;
        private bool isMiddleClickDragging = false;
        private float middleClickDragOriginY;

        private void Start()
        {
            initialDistance = distance;
            initialHeightOffset = heightOffset;
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            initialTargetPosition = target.position;

            Vector3 angles = transform.eulerAngles;
            currentX = angles.y;
            currentY = angles.x;

            if (target != null)
                targetAnimator = target.GetComponent<Animator>();
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            // 좌클릭 드래그 시작/종료
            if (mouse.leftButton.wasPressedThisFrame)
            {
                isDragging = true;
                previousMousePosition = mouse.position.ReadValue();
                velocityX = 0f;
                velocityY = 0f;
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                isDragging = false;
            }

            if (isDragging)
            {
                Vector2 currentMousePosition = mouse.position.ReadValue();
                Vector2 delta = currentMousePosition - previousMousePosition;

                if (delta.sqrMagnitude > 0.01f)
                {
                    float targetVX = delta.x * xSensitivity * rotationSpeedMultiplier * 10f;
                    float targetVY = delta.y * ySensitivity * rotationSpeedMultiplier * 10f;

                    velocityX = Mathf.Lerp(velocityX, targetVX, Time.deltaTime * rotationDecay);
                    velocityY = Mathf.Lerp(velocityY, targetVY, Time.deltaTime * rotationDecay);

                    previousMousePosition = currentMousePosition;
                }
            }
            else
            {
                // 감쇠 적용 (회전 유지 후 서서히 멈춤)
                velocityX = Mathf.Lerp(velocityX, 0f, Time.deltaTime * rotationDecay);
                velocityY = Mathf.Lerp(velocityY, 0f, Time.deltaTime * rotationDecay);
            }

            // 회전 적용
            currentX += velocityX * Time.deltaTime;
            currentY -= velocityY * Time.deltaTime;
            currentY = Mathf.Clamp(currentY, -90f, 90f);

            // 중클릭으로 높이 조절
            if (mouse.middleButton.wasPressedThisFrame)
            {
                isMiddleClickDragging = true;
                middleClickDragOriginY = mouse.position.ReadValue().y;
            }
            else if (mouse.middleButton.wasReleasedThisFrame)
            {
                isMiddleClickDragging = false;
            }

            if (isMiddleClickDragging)
            {
                float currentYPos = mouse.position.ReadValue().y;
                float yDiff = (currentYPos - middleClickDragOriginY) * yAdjustmentSpeed * Time.deltaTime;
                heightOffset -= yDiff;
                middleClickDragOriginY = currentYPos;
            }

            // 휠 줌
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
            {
                distance -= scroll * zoomSpeed * 12f * 0.01f;
                distance = Mathf.Clamp(distance, 1f, 100f);
            }

            // 우클릭으로 리셋
            if (mouse.rightButton.wasPressedThisFrame)
            {
                ResetCamera();
            }

            UpdateCameraPosition();
        }

        private void UpdateCameraPosition()
        {
            Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);
            Vector3 offset = new Vector3(0f, heightOffset, 0f);
            transform.position = target.position + offset - rotation * Vector3.forward * distance;
            transform.LookAt(target.position + offset);
        }

        private void ResetCamera()
        {
            distance = initialDistance;
            heightOffset = initialHeightOffset;
            transform.position = initialPosition;
            transform.rotation = initialRotation;
            currentX = initialRotation.eulerAngles.y;
            currentY = initialRotation.eulerAngles.x;
            target.position = initialTargetPosition;

            if (targetAnimator != null)
            {
                targetAnimator.Play(targetAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash, -1, 0f);
            }
        }
    }
}
