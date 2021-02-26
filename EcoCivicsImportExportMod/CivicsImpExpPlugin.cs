using System;
using System.Linq;
using System.IO;

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
    using Gameplay.Civics;
    using Gameplay.Civics.Laws;
    using Gameplay.Civics.Titles;
    using Gameplay.Civics.Demographics;
    using Gameplay.Civics.Constitutional;
    using Gameplay.Civics.Districts;
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

        [ChatSubCommand("Civics", "Exports a civic object to a json file.", ChatAuthorizationLevel.Admin)]
        public static void Export(User user, string type, int id)
        {
            Registrar registrar;
            switch (type)
            {
                case "law": registrar = Registrars.Get<Law>(); break;
                case "electionprocess": registrar = Registrars.Get<ElectionProcess>(); break;
                case "electedtitle": registrar = Registrars.Get<ElectedTitle>(); break;
                case "demographic": registrar = Registrars.Get<Demographic>(); break;
                case "constitution": registrar = Registrars.Get<Constitution>(); break;
                case "amendment": registrar = Registrars.Get<ConstitutionalAmendment>(); break;
                case "districtmap": registrar = Registrars.Get<DistrictMap>(); break;
                default:
                    user.Player.Msg(new LocString($"Unknown civic type '{type}' (expecting one of 'law', 'electionprocess', 'electedtitle', 'demographic', 'constitution', 'amendment')"));
                    return;
            }
            var obj = registrar.GetById(id);
            if (obj == null)
            {
                user.Player.Msg(new LocString($"Failed to export {type}: none found by id {id}"));
                return;
            }
            string outPath = Path.Combine("civics", $"{type}-{id}.json");
            try
            {
                Exporter.Export(obj, outPath);
            }
            catch (Exception ex)
            {
                user.Player.Msg(new LocString($"Failed to export {type}: {ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }
            user.Player.Msg(new LocString($"Exported {type} {id} to '{outPath}'"));
        }

        #endregion

        #region Importing

        private static WorldObject FindFreeWorldObjectForCivic(Type civicType)
        {
            var worldObjectManager = ServiceHolder<IWorldObjectManager>.Obj;
            var relevantWorldObjects = worldObjectManager.All
                .Where((worldObject) => worldObject.HasComponent<CivicObjectComponent>())
                .Select((worldObject) => (worldObject, worldObject.GetComponent<CivicObjectComponent>()))
                .Where((worldObjectAndComp) => worldObjectAndComp.Item2.ObjectType.IsAssignableFrom(civicType))
                .Where((worldObjectAndComp) => worldObjectAndComp.Item2.UsedSlots < worldObjectAndComp.Item2.MaxCount);
            return relevantWorldObjects.FirstOrDefault().worldObject;
        }

        [ChatSubCommand("Civics", "Imports a civic object from a json file.", ChatAuthorizationLevel.Admin)]
        public static void Import(User user, string filename)
        {
            string inPath = Path.Combine("civics", filename);
            IHasID obj;
            try
            {
                obj = Importer.Import(inPath);
            }
            catch (Exception ex)
            {
                user.Player.Msg(new LocString($"Failed to import civic: {ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }
            var worldObject = FindFreeWorldObjectForCivic(obj.GetType());
            if (worldObject == null)
            {
                user.Player.Msg(new LocString($"Failed to import civic: no world objects found with available space for the civic"));
                return;
            }
            var proposable = obj as IProposable;
            proposable.SetHostObject(worldObject);
            user.Player.Msg(new LocString($"Imported {proposable.UILink()} from '{inPath}' onto {worldObject.UILink()}"));
        }

        #endregion
    }
}