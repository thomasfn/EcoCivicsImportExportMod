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

            var electionProcessRegistrar = Registrars.Get<ElectionProcess>();
            var electionProcess = electionProcessRegistrar.GetById(1) as ElectionProcess;
            Exporter.Export(electionProcess, Path.Combine("civics", $"election-process-{1}.json"));
        }

        [ChatCommand("Performs an export of a particular civic object.")]
        public static void ExportCivic(User user) { }

        [ChatSubCommand("ExportCivic", "Performs an export of a particular election process.", ChatAuthorizationLevel.Admin)]
        public static void ExportCivicElectionProcess(User user, int id)
        {
            var electionProcessRegistrar = Registrars.Get<ElectionProcess>();
            var electionProcess = electionProcessRegistrar.GetById(id) as ElectionProcess;
            if (electionProcess == null)
            {
                user.Player.Msg(new LocString($"Failed to export election process: none found by id {id}"));
                return;
            }
            string outPath = Path.Combine("civics", $"election-process-{id}.json");
            try
            {
                Exporter.Export(electionProcess, outPath);
            }
            catch (Exception ex)
            {
                user.Player.Msg(new LocString($"Failed to export election process: ${ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }
            user.Player.Msg(new LocString($"Exported election process {id} to '{outPath}'"));
        }
    }
}