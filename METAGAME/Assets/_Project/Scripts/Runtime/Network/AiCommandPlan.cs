using System;

namespace Metagame.Network
{
    [Serializable]
    public sealed class AiCommandStep
    {
        public string action;
        public bool approach;
        public string destination = "default";
    }
    [Serializable]
    public sealed class AiCommandAssignment
    {
        public string recipients;
        public AiCommandStep[] steps;
    }
    public static class AiCommandPlan
    {
        public static bool Action(string action) => AiCompanionController.IsSocial(action) || AiWorkTask.Supports(action)
            || action == "follow" || action == "gather" || action == "wait" || action == "cancel" || action == "pull_pin";
        public static bool Recipients(string value) => value == "default" || value == "all" || value == "one" || value == "two"
            || value == "remaining" || value == "red" || value == "blue" || value == "yellow" || value == "green";
        public static bool Destination(string value) => value == "default" || value == "speaker" || value == "aim" || value == "assembly" || value == "high";
        public static bool Continuous(string action) => action == "follow" || action == "wait" || action == "defend_meteors"
            || action == "grab_part" || action == "hold_tractor" || action == "lift_part";
        public static bool ValidSteps(AiCommandStep[] steps)
        {
            if (steps == null || steps.Length == 0 || steps.Length > 4) return false;
            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                if (step == null || !Action(step.action) || !Destination(step.destination)
                    || (step.approach && !AiCompanionController.IsSocial(step.action))
                    || (i < steps.Length - 1 && (Continuous(step.action) || step.action == "cancel"))) return false;
            }
            return true;
        }
        public static bool Valid(AiCommandAssignment[] orders)
        {
            if (orders == null || orders.Length == 0 || orders.Length > 3) return false;
            for (int i = 0; i < orders.Length; i++)
                if (orders[i] == null || !Recipients(orders[i].recipients) || !ValidSteps(orders[i].steps)
                    || (i == 0 && orders[i].recipients == "remaining")) return false;
            return true;
        }
    }
}
