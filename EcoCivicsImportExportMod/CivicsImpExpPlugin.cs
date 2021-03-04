using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;

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
        private static IReadOnlyDictionary<string, Registrar> civicTypeToRegistrar;

        public const string ImportExportDirectory = "civics";

        public string GetStatus()
        {
            return "Idle";
        }

        public void Initialize(TimedTask timer)
        {
            civicTypeToRegistrar = new Dictionary<string, Registrar>
            {
                {"law",             Registrars.Get<Law>() },
                {"electionprocess", Registrars.Get<ElectionProcess>() },
                {"electedtitle",    Registrars.Get<ElectedTitle>() },
                {"demographic",     Registrars.Get<Demographic>() },
                {"constitution",    Registrars.Get<Constitution>() },
                {"amendment",       Registrars.Get<ConstitutionalAmendment>() },
                {"districtmap",     Registrars.Get<DistrictMap>() },
            };
            Logger.Info("Initialized and ready to go");
            Directory.CreateDirectory(ImportExportDirectory);
        }

        private static bool TryGetRegistrarForCivicType(User user, string type, out Registrar registrar)
        {
            if (civicTypeToRegistrar.TryGetValue(type, out registrar)) { return true; }
            user.Player.Msg(new LocString($"Unknown civic type '{type}' (expecting one of {string.Join(", ", civicTypeToRegistrar.Keys.Select(type => $"'{type}'"))})"));
            registrar = null;
            return false;
        }

        #region Exporting

        [ChatSubCommand("Civics", "Exports a civic object to a json file.", ChatAuthorizationLevel.Admin)]
        public static void Export(User user, string type, int id)
        {
            if (!TryGetRegistrarForCivicType(user, type, out var registrar)) { return; }
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

        private static void ExportAllOfInternal(User user, string type, ref int successCount, ref int failCount)
        {
            if (!TryGetRegistrarForCivicType(user, type, out var registrar)) { return; }
            foreach (var obj in registrar.All())
            {
                string outPath = Path.Combine(ImportExportDirectory, $"{type}-{obj.Id}.json");
                try
                {
                    Exporter.Export(obj, outPath);
                    ++successCount;
                }
                catch (Exception ex)
                {
                    user.Player.Msg(new LocString($"Failed to export {type} {obj.Id}: {ex.Message}"));
                    Logger.Error(ex.ToString());
                    ++failCount;
                }
            }
        }

        [ChatSubCommand("Civics", "Exports all civic objects of a kind to json files.", ChatAuthorizationLevel.Admin)]
        public static void ExportAllOf(User user, string type)
        {
            int successCount = 0, failCount = 0;
            ExportAllOfInternal(user, type, ref successCount, ref failCount);
            if (failCount > 0)
            {
                if (successCount == 0)
                {
                    user.Player.Msg(new LocString($"Failed to export all {failCount} of {type}"));
                }
                else
                {
                    user.Player.Msg(new LocString($"Succesfully exported {successCount} of {type}, but failed to export {failCount} of {type}"));
                }
            }
            else
            {
                user.Player.Msg(new LocString($"Succesfully exported all {successCount} of {type}"));
            }
        }

        [ChatSubCommand("Civics", "Exports all civic objects to json files.", ChatAuthorizationLevel.Admin)]
        public static void ExportAll(User user)
        {
            int successCount = 0, failCount = 0;
            foreach (var type in civicTypeToRegistrar.Keys)
            {
                ExportAllOfInternal(user, type, ref successCount, ref failCount);
            }
            if (failCount > 0)
            {
                if (successCount == 0)
                {
                    user.Player.Msg(new LocString($"Failed to export all {failCount} civic objects"));
                }
                else
                {
                    user.Player.Msg(new LocString($"Succesfully exported {successCount} civic objects, but failed to export {failCount} of civic objects"));
                }
            }
            else
            {
                user.Player.Msg(new LocString($"Succesfully exported all {successCount} civic objects"));
            }
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
        public static void Import(User user, string source)
        {
            IHasID obj;
            try
            {
                obj = Importer.Import(source);
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
                Importer.Cleanup(obj);
                user.Player.Msg(new LocString($"Failed to import civic: no world objects found with available space for the civic"));
                return;
            }
            var proposable = obj as IProposable;
            proposable.SetHostObject(worldObject);
            user.Player.Msg(new LocString($"Imported {proposable.UILink()} from '{source}' onto {worldObject.UILink()}"));
        }

        #endregion
    }
}