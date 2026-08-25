namespace GameMain2.Framework.Audio
{
    public enum SoundId
    {
        None = 0,
        MainMenuBgm = 1,
        BossBattleBgm = 2,
        Hit = 3,
        Defence = 4,
        Parry = 5,
        PlayerSingleAttack = 6,
        Footstep = 7,
        PlayerSpearAttack = 8,
        EnemySingleAttack = 9,
        EnemyGreatSwordAttack = 10,
        Drink = 11,
        BattleBgm = 12,
        BirdieEnv = 13,
        UiClick = 1000
    }

    public enum SoundCategory
    {
        Bgm = 0,
        Sfx = 1,
        Ambient = 2
    }

    public enum SoundSpatialMode
    {
        TwoDimensional = 0,
        WorldPosition = 1,
        FollowTarget = 2
    }

    public enum SoundPlaybackState
    {
        Loading = 0,
        Playing = 1,
        FadingOut = 2,
        Completed = 3,
        Canceled = 4,
        Failed = 5
    }
}
