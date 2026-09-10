using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.Gameplay
{
    /// <summary>Isolated art-scene viewer. Does not start gameplay, networking or alter global gravity.</summary>
    [DisallowMultipleComponent]
    public sealed class Stage1ConceptExplorer : MonoBehaviour
    {
        [SerializeField] private Transform[] _viewpoints;
        [SerializeField] private float _moveSpeed = 12f;
        [SerializeField] private RenderPipelineAsset _conceptPipeline;
        private RenderPipelineAsset _previousPipeline;
        private bool _pipelineApplied;
        private bool _showHelp = true;

        public void Configure(Transform[] viewpoints, RenderPipelineAsset pipeline)
        {
            _viewpoints = viewpoints;
            _conceptPipeline = pipeline;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || _conceptPipeline == null) return;
            _previousPipeline = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = _conceptPipeline;
            _pipelineApplied = true;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.H)) _showHelp = !_showHelp;
            for (int i = 0; _viewpoints != null && i < _viewpoints.Length && i < 5; i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                    transform.SetPositionAndRotation(_viewpoints[i].position, _viewpoints[i].rotation);
            }

            bool looking = Input.GetMouseButton(1) && !Input.GetKey(KeyCode.Escape);
            Cursor.lockState = looking ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !looking;
            if (!looking) return;

            Vector3 angles = transform.eulerAngles;
            float pitch = angles.x > 180f ? angles.x - 360f : angles.x;
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 2f, -85f, 85f);
            transform.rotation = Quaternion.Euler(pitch, angles.y + Input.GetAxisRaw("Mouse X") * 2f, 0f);
            Vector3 movement = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) movement += transform.forward;
            if (Input.GetKey(KeyCode.S)) movement -= transform.forward;
            if (Input.GetKey(KeyCode.D)) movement += transform.right;
            if (Input.GetKey(KeyCode.A)) movement -= transform.right;
            if (Input.GetKey(KeyCode.E)) movement += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) movement -= Vector3.up;
            transform.position += Vector3.ClampMagnitude(movement, 1f) * _moveSpeed
                * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f) * Time.unscaledDeltaTime;
        }

        private void OnDisable()
        {
            if (_pipelineApplied)
            {
                QualitySettings.renderPipeline = _previousPipeline;
                _pipelineApplied = false;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnGUI()
        {
            if (!_showHelp) return;
            GUI.Box(new Rect(20, 20, 595, 66),
                "NOT THIS PLANET  /  STAGE 01 CONCEPT EXPLORER\n"
                + "1 Overview   2 Camp   3 Assembly   4 Hangar   5 Hill\n"
                + "Hold RMB + WASD to fly  |  Q/E down/up  |  Shift faster  |  H hide");
        }
    }
}
