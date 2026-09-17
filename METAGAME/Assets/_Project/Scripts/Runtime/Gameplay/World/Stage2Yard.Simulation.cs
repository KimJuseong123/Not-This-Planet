using UnityEngine;

namespace Metagame.Gameplay.Stage2
{
    public sealed partial class Stage2Yard
    {
        public bool GasActive => _state.GasEnabled && _state.GasClock % 40f >= 6f && _state.GasClock % 40f < 23f;
        public bool GasWarning => _state.GasEnabled && _state.GasClock % 40f < 6f;

        void FixedUpdate()
        {
            if (!_authority || _state.Launched) return;
            float dt = Time.fixedDeltaTime;
            YardCommand command = default;
            if (_state.Pilot != 0 && _workers.TryGetValue(_state.Pilot, out var pilot))
            {
                if (pilot.Rig == null || pilot.Rig.Downed || Vector3.Distance(pilot.Rig.FootPosition, _seat.position) > 4f)
                    _state.Pilot = 0;
                else if (Time.time - pilot.LastInput < .35f) command = pilot.Command;
            }
            var speed = new Vector3(15f, 3f, 2.5f) * (command.Slow ? .22f : 1f);
            _craneVelocity = Vector3.MoveTowards(_craneVelocity, Vector3.Scale(command.Axes, speed), dt * 30f);
            _state.Yaw += _craneVelocity.x * dt;
            _state.Radius = Mathf.Clamp(_state.Radius + _craneVelocity.y * dt, Stage2Rules.RadiusMin, Stage2Rules.RadiusMax);
            _state.Height = Mathf.Clamp(_state.Height + _craneVelocity.z * dt, Stage2Rules.HookMin, Stage2Rules.HookMax);
            _hook.position = Stage2Rules.Hook(_cranePivot.position, _state.Yaw, _state.Radius, _state.Height);
            if (_state.Attached >= 0 && !_state.Parts[_state.Attached].Secured)
            {
                var body = _parts[_state.Attached];
                Vector3 desired = _hook.position - Vector3.up * 1.6f;
                // Damped physical suspension; collisions remain active on the host.
                body.AddForce((desired - body.position) * 22f - body.linearVelocity * 8f - Physics.gravity, ForceMode.Acceleration);
                body.AddTorque(Vector3.Cross(body.transform.up, Vector3.up) * 12f - body.angularVelocity * 5f, ForceMode.Acceleration);
            }
            MoveLift(dt);
            MoveLadder(dt);
            if (_state.GasEnabled) _state.GasClock += dt;
            foreach (var item in _workers)
            {
                var w = item.Value;
                if (w.Rig == null) continue;
                w.Rig.WorkstationMovementLocked = item.Key == _state.Pilot || item.Key == _state.Climber;
                if (Time.time - w.LastInput > .35f || w.Rig.Downed) w.Command = default;
                UpdateContamination(w, dt);
            }
            foreach (var w in _workers.Values) Spray(w, dt);
            for (int i = 0; i < _parts.Length; i++)
                if (!_state.Parts[i].Secured && (_parts[i].position.y < -5f || Vector3.Distance(_parts[i].position, _cranePivot.position) > 36f))
                { if (_state.Attached == i) _state.Attached = -1; RecoverParts(); break; }
        }

        void MoveLift(float dt)
        {
            float previous = _state.LiftHeight;
            _state.LiftHeight = Mathf.MoveTowards(previous, _state.LiftGoal, 2f * dt);
            float delta = _state.LiftHeight - previous;
            if (Mathf.Abs(delta) < .00001f) return;
            foreach (var w in _workers.Values)
            {
                if (w.Rig == null) continue;
                var foot = w.Rig.FootPosition;
                var offset = foot - _lift.position;
                if (Mathf.Abs(offset.x) < 1.8f && Mathf.Abs(offset.z) < 1.8f && offset.y > -.3f && offset.y < 1f)
                    w.Rig.Doll.Teleport(w.Rig.Position + Vector3.up * delta, w.Rig.Hips.rotation);
            }
            _lift.position += Vector3.up * delta;
        }

        bool LadderOccupied()
        {
            if (_state.Climber != 0) return true;
            foreach (var w in _workers.Values)
            {
                if (w.Rig == null) continue;
                Vector3 d = w.Rig.FootPosition - _ladder.position;
                if (Mathf.Abs(d.x) < 2.2f && Mathf.Abs(d.z) < 2.2f && d.y > .35f && d.y < _state.LadderHeight + 2f) return true;
            }
            return false;
        }

        void TryPlaceLadder(Worker w)
        {
            Vector3 point = w.Rig.FootPosition + w.Rig.FacingDirection * 2.5f;
            if (!Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)
                || Vector3.Dot(hit.normal, Vector3.up) < .98f || hit.point.y > .3f) return;
            // A broad, flat support footprint; avoid the rocket and lift corridors.
            if (Vector3.Distance(new Vector3(hit.point.x, 0, hit.point.z), new Vector3(_launch.position.x, 0, _launch.position.z)) < 3.3f) return;
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    if (!Physics.Raycast(hit.point + new Vector3(x * 1.5f, 1f, z * 1.5f), Vector3.down, out var corner, 2f)
                        || Mathf.Abs(corner.point.y - hit.point.y) > .1f) return;
            int next = 0; while (next < _state.Parts.Length - 1 && _state.Parts[next].Secured) next++;
            _state.LadderHeight = 1f + next * 3f;
            _state.LadderPosition = hit.point;
            _state.LadderCarrier = 0;
            _state.LadderDeployed = true;
        }

        void MoveLadder(float dt)
        {
            if (_state.LadderCarrier != 0 && _workers.TryGetValue(_state.LadderCarrier, out var carrier))
            {
                if (carrier.Rig == null || carrier.Rig.Downed)
                { _state.LadderCarrier = 0; _state.LadderDeployed = true; _state.LadderPosition = _ladderHome; }
                else _state.LadderPosition = carrier.Rig.FootPosition + carrier.Rig.FacingDirection * 2.5f;
            }
            if (_state.Climber == 0 || !_workers.TryGetValue(_state.Climber, out var climber) || climber.Rig == null) return;
            if (climber.Rig.Downed) { _state.Climber = 0; return; }
            _climbTime += dt;
            float t = Mathf.Clamp01(_climbTime / Mathf.Max(1f, Vector3.Distance(_climbStart, _climbEnd) / 2f));
            Vector3 foot = Vector3.Lerp(_climbStart, _climbEnd, Mathf.SmoothStep(0, 1, t));
            climber.Rig.Doll.Teleport(climber.Rig.Position + foot - climber.Rig.FootPosition, climber.Rig.Hips.rotation);
            if (t >= 1f) { climber.Rig.WorkstationMovementLocked = false; _state.Climber = 0; }
        }

        void UpdateContamination(Worker w, float dt)
        {
            bool exposed = false;
            if (GasActive) foreach (var zone in _gasZones) if (zone.bounds.Contains(w.Rig.Position)) exposed = true;
            if (exposed) w.State.Contamination = Mathf.Clamp01(w.State.Contamination + dt / (_workers.Count <= 1 ? 90f : 55f));
            if (Vector3.Distance(w.Rig.FootPosition, _shower.position) < 2f)
                w.State.Contamination = Mathf.Max(0, w.State.Contamination - dt * .22f);
            if (w.State.Contamination >= 1 && !w.Rig.Downed) w.Rig.GoDown("유독물질 오염 — 동료 제독 후 G 구조");
        }

        public void ClearContaminationAfterRespawn(PlayerRig rig)
        {
            // Walking into the preparation area is not a decontamination action.
            // GameSession emits this only after actual recovery, on the authoritative instance.
            if (!_authority || rig == null) return;
            foreach (var worker in _workers.Values)
                if (worker.Rig == rig) { worker.State.Contamination = 0; worker.State.Spraying = false; }
        }

        void Spray(Worker w, float dt)
        {
            w.State.Spraying = w.Rig != null && !w.Rig.Downed && w.Command.Primary && w.State.Tool == YardTool.Sprayer
                && _state.Climber != w.State.Id && _state.Pilot != w.State.Id;
            if (!w.State.Spraying) return;
            Vector3 from = w.Rig.Position + Vector3.up * .45f;
            Vector3 aim = w.Command.Aim.normalized;
            Vector3 end = from + aim * Stage2Rules.SprayRange;
            float nearest = Stage2Rules.SprayRange;
            foreach (var hit in Physics.RaycastAll(from, aim, nearest, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<PlayerRig>() != w.Rig && hit.distance < nearest) { nearest = hit.distance; end = hit.point; }
            w.State.SprayStart = from; w.State.SprayEnd = end;
            foreach (var other in _workers.Values)
            {
                if (other == w || other.Rig == null) continue;
                Vector3 target = other.Rig.Position;
                Vector3 delta = target - from;
                if (delta.magnitude > Stage2Rules.SprayRange || Vector3.Angle(aim, delta) > 12f
                    || !ClearLine(from, target, w.Rig, other.Rig.transform)) continue;
                other.State.Contamination = Mathf.Max(0, other.State.Contamination - dt * .18f);
            }
        }

        void LateUpdate()
        {
            ReplicaWindow(out var a, out var b, out float t);
            if (_authority) { a = b = _state; t = 1; }
            float yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, t);
            _arm.localRotation = Quaternion.Euler(0, yaw, 0);
            _hook.position = Stage2Rules.Hook(_cranePivot.position, yaw, Mathf.Lerp(a.Radius,b.Radius,t), Mathf.Lerp(a.Height,b.Height,t));
            _ladder.position = Vector3.Lerp(a.LadderPosition,b.LadderPosition,t);
            // Builder names identify only authored visual subparts, never network identities.
            if (_platform != null) _platform.localPosition = Vector3.up * (_state.LadderDeployed ? _state.LadderHeight : .8f);
            if (_ladderTop != null) _ladderTop.localPosition = new Vector3(0, _state.LadderHeight + .6f, -1.4f);
            foreach (var collider in _ladderColliders) collider.enabled = _state.LadderDeployed;
            float ladderHeight = _state.LadderDeployed ? _state.LadderHeight : .8f;
            foreach (var rail in _ladderRails)
            {
                var pos = rail.localPosition; pos.y = ladderHeight * .5f; rail.localPosition = pos;
                rail.localScale = new Vector3(.13f, ladderHeight, .13f);
                rail.localRotation = Quaternion.Euler(pos.z < 0 ? -8f : 8f,0,0);
            }
            foreach (var step in _ladderSteps) step.gameObject.SetActive(step.localPosition.y < ladderHeight && _state.LadderDeployed);
            bool liftMoving = Mathf.Abs(_state.LiftHeight - _state.LiftGoal) > .05f;
            if (_liftGate != null) _liftGate.gameObject.SetActive(liftMoving);
            if (_upperGate != null) _upperGate.gameObject.SetActive(_state.LiftHeight < 7.95f);
            if (_lowerGate != null) _lowerGate.gameObject.SetActive(_state.LiftHeight > .05f);
            foreach (var cloud in _gasParticles)
            {
                if (cloud == null) continue;
                if (GasActive && !cloud.isPlaying) cloud.Play();
                else if (!GasActive && cloud.isPlaying) cloud.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            _wire.SetPosition(0, new Vector3(_hook.position.x, 20f, _hook.position.z));
            _wire.SetPosition(1, _hook.position);
            if (!_authority)
            {
                var liftPosition = _lift.position; liftPosition.y = Mathf.Lerp(a.LiftHeight,b.LiftHeight,t) + .2f; _lift.position = liftPosition;
                for (int i = 0; i < _parts.Length; i++)
                {
                    _parts[i].position = Vector3.Lerp(a.Parts[i].Position, b.Parts[i].Position, t);
                    _parts[i].rotation = Quaternion.Slerp(a.Parts[i].Rotation, b.Parts[i].Rotation, t);
                }
            }
            for (int i = 0; i < _lids.Length; i++)
                _lids[i].localRotation = Quaternion.Euler(_state.Parts[i].Open ? 110f : 0, 0, 0);
            for (int i = 0; i < _boltRenderers.Length; i++)
            {
                var part = _state.Parts[i / 3];
                float value = i % 3 == 0 ? part.A : i % 3 == 1 ? part.B : part.C;
                _boltColor.SetColor("_BaseColor", Color.Lerp(new Color(.95f,.55f,.1f), new Color(.15f,.9f,.5f), value));
                _boltRenderers[i].SetPropertyBlock(_boltColor);
            }
        }
    }
}
