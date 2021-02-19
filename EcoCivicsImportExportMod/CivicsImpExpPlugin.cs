using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Plugins.Interfaces;
    using Core.Utils;
    using Core.Systems;
    using Core.IoC;

    using Shared.Localization;

    using Gameplay.Players;
    using Gameplay.Systems.Chat;
    using Gameplay.Systems.TextLinks;
    using Gameplay.Civics.Laws;
    using Gameplay.Civics.Misc;
    using Gameplay.Objects;
    using Gameplay.Components;

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

        [ChatSubCommand("Civics", "Performs an export of a particular law.", ChatAuthorizationLevel.Admin)]
        public static void ExportLaw(User user, int id)
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
                user.Player.Msg(new LocString($"Failed to export law: {ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }
            user.Player.Msg(new LocString($"Exported law {id} to '{outPath}'"));
        }

        #endregion

        #region Importing

        private static WorldObject FindFreeWorldObjectForCivic<TCivic>() where TCivic : SimpleProposable
        {
            var worldObjectManager = ServiceHolder<IWorldObjectManager>.Obj;
            var relevantWorldObjects = worldObjectManager.All
                .Where((worldObject) => worldObject.HasComponent<CivicObjectComponent>())
                .Select((worldObject) => (worldObject, worldObject.GetComponent<CivicObjectComponent>()))
                .Where((worldObjectAndComp) => worldObjectAndComp.Item2.ObjectType.IsAssignableFrom(typeof(TCivic)))
                .Where((worldObjectAndComp) => worldObjectAndComp.Item2.UsedSlots < worldObjectAndComp.Item2.MaxCount);
            return relevantWorldObjects.FirstOrDefault().worldObject;
        }

        [ChatSubCommand("Civics", "Performs an import of a particular law.", ChatAuthorizationLevel.Admin)]
        public static void ImportLaw(User user, string filename)
        {
            var worldObject = FindFreeWorldObjectForCivic<Law>();
            if (worldObject == null)
            {
                user.Player.Msg(new LocString($"Failed to import law: no world objects found with available space for the civic"));
                return;
            }
            string inPath = Path.Combine("civics", filename);
            Law law;
            try
            {
                law = Importer.Import<Law>(inPath);
                law.Creator = user;
            }
            catch (Exception ex)
            {
                user.Player.Msg(new LocString($"Failed to import law: {ex.Message}"));
                return;
            }
            law.SetHostObject(worldObject);
            law.MarkDirty();
            user.Player.Msg(new LocString($"Imported law {law.UILink()} from '{inPath}' onto {worldObject.UILink()}"));
        }

        #endregion
    }
}