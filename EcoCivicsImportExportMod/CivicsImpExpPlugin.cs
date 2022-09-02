using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Plugins.Interfaces;
    using Core.Utils;
    using Core.Systems;

    using Shared.Utils;

    public class CivicsImpExpPlugin : Singleton<CivicsImpExpPlugin>, IModKitPlugin, IInitializablePlugin
    {
        public const string ImportExportDirectory = "civics";

        public List<IHasID> LastImport { get; } = new List<IHasID>();

        public string GetStatus()
        {
            return "Idle";
        }

        public string GetCategory()
        {
            return "Civics";
        }

        public void Initialize(TimedTask timer)
        {
            Directory.CreateDirectory(ImportExportDirectory);
            Logger.Info("Initialized and ready to go");
        }
    }
}