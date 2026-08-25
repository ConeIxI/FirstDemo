namespace GameMain2.Scripts.Character
{
    public enum PlayerState
    {
        Locomotion, //待机和移动
        RunStop,    //急停
        Dodge,       //翻滚（闪避）
        Jump,       //跳跃（主动跳跃已停用，保留枚举值避免序列化偏移）
        AirDown,    //下落状态
        Attack,     //普通攻击状态
        Defence,    //格挡
        Parry,      //弹反成功
        Unbalance,  //失衡
        Dead,       //死亡
        Execution,  //处决
        ItemDrink,  //使用消耗品
        ItemGet,    //拾取地面物品
    }

    public enum PlayerHitDirection
    {
        Front = 0,
        Right = 1,
        Left = 2,
        Back = 3
    }
}
