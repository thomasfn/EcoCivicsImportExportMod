using System;
using System.Linq;
using System.IO;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Plugins.Interfaces;
    using Core.Utils;
    using Core.Systems;

    using Gameplay.Players;
    using Gameplay.Systems.Chat;
    using Gameplay.Civics;
    using Gameplay.Civics.Demographics;
    using Gameplay.Civics.Titles;
    using Gameplay.Civics.Constitutional;
    using Gameplay.Civics.Laws;

    using Shared.Localization;
    using Shared.Utils;

    public class CivicsImpExpPlugin : IModKitPlugin, IInitializablePlugin, IChatCommandHandler
    {
        public string GetStatus()
        {
            return "Idle";
        }

        public void Initialize(TimedTask timer)
        {
            Logger.Info("Initialized and ready to go");
        }

        #region Exporting

        [ChatCommand("Performs an export of a particular civic object.")]
        public static void ExportCivic(User user) { }

        [ChatSubCommand("ExportCivic", "Performs an export of a particular law.", ChatAuthorizationLevel.Admin)]
        public static void ExportCivicLaw(User user, int id)
        {
            var lawRegistrar = Registrars.Get<Law>();
            var law = lawRegistrar.GetById(id) as Law;
            if (law == null)
            {
                user.Player.Msg(new LocString($"Failed to export law: none found by id {id}"));
                return;
            }
            string outPath = Path.Combine("civics", $"law-{id}.json");
            try
            {
                Exporter.Export(law, outPath);
            }
            catch (Exception ex)
            {
                user.Player.Msg(new LocString($"Failed to export law: ${ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }
            user.Player.Msg(new LocString($"Exported law {id} to '{outPath}'"));
        }

        #endregion

        #region Importing

        [ChatCommand("Performs an import of a particular civic object.")]
        public static void ImportCivic(User user) { }

        [ChatSubCommand("ImportCivic", "Performs an import of a particular law.", ChatAuthorizationLevel.Admin)]
        public static void ImportCivicLaw(User user, string filename)
        {
            string inPath = Path.Combine("civics", filename);
            Law law;
            try
            {
                law = Importer.Import<Law>(inPath);
                law.Creator = user;
            }
            catch (Exception ex)
            {
                user.Player.Msg(new LocString($"Failed to import law: ${ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }
            user.Player.Msg(new LocString($"Imported law {law.Id} from '{inPath}'"));
        }

        #endregion
    }
}