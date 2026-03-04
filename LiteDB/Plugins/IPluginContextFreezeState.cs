namespace LiteDB.Plugins
{
    internal interface IPluginContextFreezeState
    {
        bool IsFrozen { get; }

        void EnsureNotFrozen();
    }
}

