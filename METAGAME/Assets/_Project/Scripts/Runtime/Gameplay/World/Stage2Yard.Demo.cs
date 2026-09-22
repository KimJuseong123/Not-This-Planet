using Metagame.Core;
using UnityEngine;

namespace Metagame.Gameplay.Stage2
{
    public sealed partial class Stage2Yard
    {
        public const float DemoMoveSeconds = 3f;
        public const float DemoOverviewSeconds = 6f;
        public const float DemoAssemblySeconds = 6f;
        RigidbodyInterpolation[] _demoInterpolation;
        public bool DemoMode { get; private set; }
        public float DemoElapsed => _state.DemoStarted && _clock != null
            ? Mathf.Max(0, (float)(_clock() - _state.DemoStart)) : 0;
        public bool DemoBlocksInput => DemoMode && (!_state.DemoStarted || DemoElapsed >= DemoMoveSeconds);
        public bool DemoCinematic => DemoMode && _state.DemoStarted && DemoElapsed >= DemoMoveSeconds;
        public float DemoEndAt => DemoMoveSeconds + DemoOverviewSeconds + _parts.Length * DemoAssemblySeconds + 3f;
        public bool DemoFinished => DemoCinematic && DemoElapsed >= DemoEndAt;
        public InputFrame FilterDemoInput(InputFrame frame) => DemoBlocksInput ? default : new InputFrame
        {
            Seq = frame.Seq, Move = frame.Move, Look = frame.Look, Jump = frame.Jump, Run = frame.Run,
            AimYaw = frame.AimYaw, AimPitch = frame.AimPitch, AimFrom = frame.AimFrom, FreeLook = frame.FreeLook
        };
        public Vector3 DemoCenter => _launch.position;
        public Vector3 DemoCrane => _seat.position;
        public int DemoPartCount => _parts.Length;
        public Vector3 DemoSocket(int index) => _sockets[index].position;
        public Vector3 DemoHome(int index) => _homes[index];
        public float DemoBoltProgress(int index) => Mathf.Clamp01((DemoElapsed - DemoMoveSeconds - DemoOverviewSeconds
            - index / 3 * DemoAssemblySeconds - 4.3f - index % 3 * .4f) / .4f);

        // Opted in only by Stage2Bootstrap. The underlying yard remains available to authoring/tests.
        public void SetDemoMode(bool enabled)
        {
            if (enabled && !DemoMode)
            {
                _demoInterpolation = new RigidbodyInterpolation[_parts.Length];
                for (int i = 0; i < _parts.Length; i++) _demoInterpolation[i] = _parts[i].interpolation;
            }
            DemoMode = enabled;
            for (int i = 0; i < _parts.Length; i++)
            {
                _parts[i].isKinematic = enabled || !_authority || _state.Parts[i].Secured || !_state.Parts[i].Open;
                if (enabled) _parts[i].interpolation = RigidbodyInterpolation.None;
                else if (_demoInterpolation != null) _parts[i].interpolation = _demoInterpolation[i];
            }
        }

        public bool TryStartDemo()
        {
            if (!DemoMode || !_authority || _state.DemoStarted || _clock == null) return false;
            _state.DemoStart = _clock();
            _state.DemoStarted = true;
            return true;
        }

        // A seekable presentation of assembly, never a real clear/launch or a submitted puzzle result.
        void PresentDemo()
        {
            if (!DemoCinematic) return;
            float assembly = DemoElapsed - DemoMoveSeconds - DemoOverviewSeconds;
            int active = Mathf.Clamp(Mathf.FloorToInt(assembly / DemoAssemblySeconds), 0, _parts.Length - 1);
            for (int i = 0; i < _parts.Length; i++)
            {
                float time = assembly - i * DemoAssemblySeconds;
                float lift = Mathf.SmoothStep(0, 1, Mathf.Clamp01(time / 1.5f));
                float travel = Mathf.SmoothStep(0, 1, Mathf.Clamp01((time - 1.5f) / 2f));
                float lower = Mathf.SmoothStep(0, 1, Mathf.Clamp01((time - 3.5f) / .8f));
                Vector3 high = Vector3.Lerp(_homes[i], _sockets[i].position, travel);
                high.y = Mathf.Max(_homes[i].y, _sockets[i].position.y) + 3f;
                Vector3 position = Vector3.Lerp(Vector3.Lerp(_homes[i], high, lift), _sockets[i].position, lower);
                _parts[i].position = position;
                _parts[i].rotation = Quaternion.Slerp(_homeRotations[i], Quaternion.identity, travel);
                _parts[i].transform.SetPositionAndRotation(_parts[i].position, _parts[i].rotation);
                _lids[i].localRotation = Quaternion.Euler(time >= 0 ? 110f : 0, 0, 0);
                for (int bolt = 0; bolt < 3; bolt++)
                {
                    float fastened = Mathf.Clamp01((time - 4.3f - bolt * .4f) / .4f);
                    _boltColor.SetColor("_BaseColor", Color.Lerp(new Color(.95f, .55f, .1f), new Color(.15f, .9f, .5f), fastened));
                    _boltRenderers[i * 3 + bolt].SetPropertyBlock(_boltColor);
                }
            }
            if (assembly < 0) return;
            _hook.position = _parts[active].position + Vector3.up * 1.6f;
            Vector3 delta = _hook.position - _cranePivot.position;
            _arm.localRotation = Quaternion.Euler(0, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, 0);
            _wire.SetPosition(0, new Vector3(_hook.position.x, 20f, _hook.position.z));
            _wire.SetPosition(1, _hook.position);
            // Stage the existing telescopic work ladder underneath the workers, not in mid-air.
            float height = _sockets[active].position.y - .8f;
            _ladder.position = _launch.position + new Vector3(0, 0, -2.3f);
            if (_platform != null) _platform.localPosition = Vector3.up * height;
            foreach (var rail in _ladderRails)
            {
                var position = rail.localPosition; position.y = height * .5f; rail.localPosition = position;
                rail.localScale = new Vector3(.13f, height, .13f);
            }
            foreach (var step in _ladderSteps) step.gameObject.SetActive(step.localPosition.y < height);
        }
    }
}
