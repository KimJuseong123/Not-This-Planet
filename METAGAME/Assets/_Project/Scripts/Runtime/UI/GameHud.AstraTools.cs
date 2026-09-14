using UnityEngine;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;

namespace Metagame.UI
{
    public partial class GameHud
    {
        void AstraToolHint(float w, float h)
        {
            if (Local == null || Local.Downed) return;
            string message = null;
            if (Local.PresentationTool is AstraHandTool tool && tool.IsHeldForView) message = tool.UseHint;
            if (Local.MountedVehicle != null && Local.MountedVehicle.TryGetComponent<AstraFuelTank>(out var tank))
                message = "견인장치 연료 " + Mathf.CeilToInt(tank.Units) + " / " + tank.Capacity;
            if (message == null) return;
            Box(new Rect(w * .5f - 265f, h * .65f, 530f, 32f), new Color(.03f, .025f, .05f, .8f));
            L(new Rect(w * .5f - 255f, h * .65f + 5f, 510f, 24f), message, small, new Color(1f, .88f, .23f));
        }
    }
}
