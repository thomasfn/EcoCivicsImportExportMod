using System;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Plugins.Interfaces;
    using Core.Utils;

    public class CivicsImpExpPlugin : IModKitPlugin, IInitializablePlugin
    {
        public string GetStatus()
        {
            return "Idle";
        }

        public void Initialize(TimedTask timer)
        {
            Logger.Info("Hello world");
        }
    }
}