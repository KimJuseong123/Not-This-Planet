namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 접합 지점의 역할. 파츠는 아래에서 위로 쌓이므로 두 가지면 충분하다.
    /// </summary>
    public enum SocketRole
    {
        /// <summary>파츠의 아래쪽 접합면. 이미 놓인 파츠의 <see cref="Top"/> 에 맞물린다.</summary>
        Base = 0,

        /// <summary>파츠의 위쪽 접합면. 위에 올라올 파츠의 <see cref="Base"/> 를 받는다.</summary>
        Top,
    }
}
