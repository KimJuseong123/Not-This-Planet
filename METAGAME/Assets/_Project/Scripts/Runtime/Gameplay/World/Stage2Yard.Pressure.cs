using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Stage2
{
    public sealed partial class Stage2Yard
    {
        [SerializeField] Stage2PressurePump[] _surgePumps = new Stage2PressurePump[0];
        readonly Dictionary<uint,float> _pressureHits = new();
        public Stage2PressurePump[] PressurePumps => _surgePumps;
        public int ActivePressurePump => _surgePumps.Length == 0 ? -1 : (int)(_state.PressureClock / 42f) % _surgePumps.Length;
        public int PressurePhase(int index)
        {
            if (!_state.GasEnabled || _state.Launched || index != ActivePressurePump || index < 0
                || _state.PressureRelief == null || index >= _state.PressureRelief.Length || _state.PressureRelief[index] > 0) return 0;
            float cycle = _state.PressureClock % 42f;
            return cycle >= 18 && cycle < 21 ? 2 : cycle >= 12 && cycle < 18 ? 1 : 0;
        }
        void TickPressureSurge(float dt)
        {
            if (!_authority || _state.Launched) return;
            for (int i=0;i<_state.PressureRelief.Length;i++)
                _state.PressureRelief[i]=Mathf.Max(0,_state.PressureRelief[i]-dt);
            if (!_state.GasEnabled || _surgePumps.Length==0) return;
            _state.PressureClock+=dt;
            int index=ActivePressurePump;
            if (PressurePhase(index)!=2 || _surgePumps[index]==null) return;
            var pump=_surgePumps[index];
            foreach(var item in _workers)
            {
                var rig=item.Value.Rig;
                if(rig==null || rig.Downed || rig.WorkstationMovementLocked || !pump.Contains(rig.Position))continue;
                if(_pressureHits.TryGetValue(item.Key,out float next) && _state.PressureClock<next)continue;
                if(!pump.ClearTo(rig))continue;
                // A bounded pulse affects all ragdoll bodies equally; no client simulation or teleport.
                // Ground braking is 30 m/s²: 2.4 m/s vanished after ~7 cm in a real
                // host process. Keep the kick local to this hazard and readable under braking.
                rig.KnockDown(pump.Direction+Vector3.up*.08f,4.5f);
                rig.Stagger(.65f);
                _pressureHits[item.Key]=_state.PressureClock+.65f;
            }
        }
    }
}
