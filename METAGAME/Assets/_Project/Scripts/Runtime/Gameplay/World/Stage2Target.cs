using UnityEngine;

namespace Metagame.Gameplay.Stage2
{
    public sealed class Stage2Target : MonoBehaviour
    {
        [SerializeField] int _id;
        [SerializeField] YardTargetKind _kind;
        [SerializeField] int _index;
        [SerializeField] string _label;
        public int Id => _id;
        public YardTargetKind Kind => _kind;
        public int Index => _index;
        public string Label => _label;
        public void Configure(int id, YardTargetKind kind, int index, string label)
        { _id = id; _kind = kind; _index = index; _label = label; }
    }
}
