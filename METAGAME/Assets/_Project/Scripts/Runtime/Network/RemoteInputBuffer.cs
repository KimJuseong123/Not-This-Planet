using System.Collections.Generic;
using Metagame.Core;

namespace Metagame.Network
{
    /// <summary>Latest movement state, with ordered one-shot actions between physics steps.</summary>
    public sealed class RemoteInputBuffer
    {
        const int Capacity = 32;
        readonly Queue<byte> _actions = new(Capacity);
        InputFrame _latest;
        byte _receivedActions;
        bool _releaseStep;
        public int OverflowCount { get; private set; }
        public void DiscardActions() { _actions.Clear(); _releaseStep = false; }

        public void Receive(InputFrame frame)
        {
            if (frame.Seq != 0 && _latest.Seq != 0 && unchecked((int)(frame.Seq - _latest.Seq)) <= 0) return;
            byte actions = (byte)((frame.TransportAction ? 1 : 0) | (frame.PingPressed ? 2 : 0)
                | (frame.FieldToggle ? 4 : 0));
            byte pressed = (byte)(actions & ~_receivedActions);
            _receivedActions = actions;
            _latest = frame;
            if (pressed == 0) return;
            if (_actions.Count < Capacity) _actions.Enqueue(pressed);
            else OverflowCount++; // Bound memory even if an owner floods reliable input.
        }

        public InputFrame Consume()
        {
            var frame = _latest;
            byte actions = 0;
            if (!_releaseStep && _actions.Count > 0) actions = _actions.Dequeue();
            // Existing consumers detect rising edges, so adjacent queued presses need a release.
            _releaseStep = actions != 0;
            frame.TransportAction = (actions & 1) != 0;
            frame.PingPressed = (actions & 2) != 0;
            frame.FieldToggle = (actions & 4) != 0;
            return frame;
        }
    }
}
