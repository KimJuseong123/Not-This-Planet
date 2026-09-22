using System;

namespace Metagame.Network
{
    /// <summary>One bounded upload, granted by the server and paced independently of FPS.</summary>
    public sealed class AiVoiceTransfer
    {
        public const int ChunkBytes = 2048;
        public const double Interval = .04, Timeout = 12;
        byte[] _bytes;
        int _offset;
        bool _approved;
        double _next, _deadline;
        public uint Id { get; private set; }
        public bool Active => _bytes != null;

        public uint Begin(byte[] bytes, double now)
        {
            Clear();
            if (++Id == 0) ++Id;
            _bytes = bytes; _deadline = now + Timeout;
            return Id;
        }

        public void Approve(uint id, bool accepted, double now)
        {
            if (!Active || id != Id) return;
            if (!accepted || now >= _deadline) { Clear(); return; }
            _approved = true; _next = now;
            _deadline = now + Timeout;
        }

        public bool Expire(double now)
        {
            if (!Active || now < _deadline) return false;
            Clear(); return true;
        }

        public bool TryNext(double now, out ArraySegment<byte> chunk, out bool last)
        {
            chunk = default; last = false;
            if (Expire(now) || !Active || !_approved || now < _next) return false;
            int count = Math.Min(ChunkBytes, _bytes.Length - _offset);
            chunk = new ArraySegment<byte>(_bytes, _offset, count);
            _offset += count;
            _next = now + Interval; // Never catch up with a burst after a slow frame.
            last = _offset == _bytes.Length;
            if (last) Clear();
            return true;
        }

        public void Clear() { _bytes = null; _offset = 0; _approved = false; }
    }
}
