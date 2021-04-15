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

        private static readonly List<IHasID> lastImport = new List<IHasID>();

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

        private static int GetUsedSlots(CivicObjectComponent civicObjectComponent, IDictionary<CivicObjectComponent, int> usedSlotsModifierDict = null)
        {
            int modifier;
            if (usedSlotsModifierDict == null || !usedSlotsModifierDict.TryGetValue(civicObjectComponent, out modifier)) { modifier = 0; }
            return civicObjectComponent.UsedSlots + modifier;
        }

        private static WorldObject FindFreeWorldObjectForCivic(Type civicType, IDictionary<CivicObjectComponent, int> usedSlotsModifierDict = null)
        {
            var worldObjectManager = ServiceHolder<IWorldObjectManager>.Obj;
            var relevantWorldObjects = worldObjectManager.All
                .Where((worldObject) => worldObject.HasComponent<CivicObjectComponent>())
                .Select((worldObject) => (worldObject, worldObject.GetComponent<CivicObjectComponent>()))
                .Where((worldObjectAndComp) => worldObjectAndComp.Item2.ObjectType.IsAssignableFrom(civicType))
                .Where((worldObjectAndComp) => GetUsedSlots(worldObjectAndComp.Item2, usedSlotsModifierDict) < worldObjectAndComp.Item2.MaxCount);
            return relevantWorldObjects.FirstOrDefault().worldObject;
        }

        private static int CountFreeSlotsForCivic(Type civicType)
        {
            var worldObjectManager = ServiceHolder<IWorldObjectManager>.Obj;
            return worldObjectManager.All
                .Where((worldObject) => worldObject.HasComponent<CivicObjectComponent>())
                .Select((worldObject) => (worldObject, worldObject.GetComponent<CivicObjectComponent>()))
                .Where((worldObjectAndComp) => worldObjectAndComp.Item2.ObjectType.IsAssignableFrom(civicType))
                .Select((worldObjectAndComp) => worldObjectAndComp.Item2.MaxCount - worldObjectAndComp.Item2.UsedSlots)
                .Sum();
        }

        [ChatSubCommand("Civics", "Imports a civic object from a json file.", ChatAuthorizationLevel.Admin)]
        public static void Import(User user, string source)
        {
            // Import the bundle
            CivicBundle bundle;
            try
            {
                bundle = Importer.ImportBundle(source);
            }
            catch (Exception ex)
            {
                user.Player.Msg(new LocString($"Failed to import bundle: {ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }

            // Check that there are enough free slots for all the civics in the bundle
            var bundledCivicsByType = bundle.Civics
                .GroupBy((bundledCivic) => bundledCivic.Type)
                .Where((grouping) => typeof(IProposable).IsAssignableFrom(grouping.Key));
            foreach (var grouping in bundledCivicsByType)
            {
                var freeSlots = CountFreeSlotsForCivic(grouping.Key);
                int importCount = grouping.Count();
                if (importCount > freeSlots)
                {
                    user.Player.Msg(new LocString($"Unable to import {importCount} of {grouping.Key.Name} (only {freeSlots} available slots for this civic type)"));
                    return;
                }
            }

            // Import the objects from the bundle
            IEnumerable<IHasID> importedObjects;
            try
            {
                importedObjects = bundle.ImportAll();
            }
            catch (Exception ex)
            {
                user.Player.Msg(new LocString($"Failed to import civic: {ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }
            lastImport.Clear();
            lastImport.AddRange(importedObjects);

            // Slot each civic into the relevant world object
            int numCivics = 0;
            IDictionary<CivicObjectComponent, int> usedSlotsModifierDict = new Dictionary<CivicObjectComponent, int>();
            foreach (var obj in importedObjects.Where((obj) => obj is IProposable && !(obj is IParentedEntry)))
            {
                var worldObject = FindFreeWorldObjectForCivic(obj.GetType(), usedSlotsModifierDict);
                if (worldObject == null)
                {
                    // This should never happen as we already checked above for free slots and early'd out, but just in case...
                    Importer.Cleanup(importedObjects);
                    user.Player.Msg(new LocString($"Failed to import civic of type '{obj.GetType().Name}': no world objects found with available space for the civic"));
                    lastImport.Clear();
                    return;
                }
                var civicObjectComponent = worldObject.GetComponent<CivicObjectComponent>();
                var proposable = obj as IProposable;
                proposable.SetHostObject(worldObject);
                if (usedSlotsModifierDict.TryGetValue(civicObjectComponent, out int currentModifier))
                {
                    usedSlotsModifierDict[civicObjectComponent] = currentModifier + 1;
                }
                else
                {
                    usedSlotsModifierDict.Add(civicObjectComponent, 1);
                }
                user.Player.Msg(new LocString($"Imported {proposable.UILink()} from '{source}' onto {worldObject.UILink()}"));
                ++numCivics;
            }
            
            // If the bundle contains more than civic, wrap it into an election
            if (numCivics > 1)
            {
                // TODO: This
                // Don't forget to add the election to lastImport!
            }
        }

        [ChatSubCommand("Civics", "Undoes the last imported civic bundle. Use with extreme care.", ChatAuthorizationLevel.Admin)]
        public static void UndoImport(User user)
        {
            Importer.Cleanup(lastImport);
            user.Player.Msg(new LocString($"Deleted {lastImport.Count} objects from the last import"));
            lastImport.Clear();
        }

        [ChatSubCommand("Civics", "Prints details about a civic bundle without actually importing anything.", ChatAuthorizationLevel.Admin)]
        public static void BundleInfo(User user, string source)
        {
            // Import the bundle
            CivicBundle bundle;
            try
            {
                bundle = Importer.ImportBundle(source);
            }
            catch (Exception ex)
            {
                user.Player.Msg(new LocString($"Failed to import bundle: {ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }

            // Print type metrics
            var bundledCivicsByType = bundle.Civics
                .GroupBy((bundledCivic) => bundledCivic.Type)
                .Where((grouping) => typeof(IProposable).IsAssignableFrom(grouping.Key));
            foreach (var grouping in bundledCivicsByType)
            {
                var freeSlots = CountFreeSlotsForCivic(grouping.Key);
                int importCount = grouping.Count();
                user.Player.Msg(new LocString($"Bundle has {importCount} of {grouping.Key.Name} (there are {freeSlots} available slots for this civic type)"));
                var subobjectsByType = grouping
                    .SelectMany((bundledCivic) => bundledCivic.InlineObjects)
                    .GroupBy((bundledCivic) => bundledCivic.Type);
                for (int i = 0, l = subobjectsByType.Count(); i < l; ++i)
                {
                    var subGrouping = subobjectsByType.Skip(i).First();
                    user.Player.Msg(new LocString($" - with {importCount} of {subGrouping.Key.Name}"));
                }
            }

            // Print reference metrics
            var importContext = new ImportContext();
            IList<object> resolvableExternalReferences = new List<object>();
            IList<CivicReference> unresolvableExternalReferences = new List<CivicReference>();
            foreach (var civicReference in bundle.ExternalReferences)
            {
                if (importContext.TryResolveReference(civicReference, out var resolvedObject))
                {
                    resolvableExternalReferences.Add(resolvedObject);
                }
                else
                {
                    unresolvableExternalReferences.Add(civicReference);
                }
            }
            if ((resolvableExternalReferences.Count + unresolvableExternalReferences.Count) == 0)
            {
                user.Player.Msg(new LocString($"Bundle has no external references."));
            }
            else
            {
                if (resolvableExternalReferences.Count > 0)
                {
                    var resRefStr = string.Join(", ", resolvableExternalReferences.Distinct().Select((obj) => obj is ILinkable linkable ? linkable.UILink().ToString() : obj.ToString()));
                    user.Player.Msg(new LocString($"Bundle has {resolvableExternalReferences.Count} references to the following: {resRefStr}"));
                    if (unresolvableExternalReferences.Count > 0)
                    {
                        var unresRefStr = string.Join(", ", unresolvableExternalReferences.Distinct().Select((civicRef) => $"{civicRef.Type} \"{civicRef.Name}\""));
                        user.Player.Msg(new LocString($"Bundle has {unresolvableExternalReferences.Count} unresolvable external references: {unresRefStr}"));
                    }
                    else
                    {
                        user.Player.Msg(new LocString($"Bundle has no unresolvable external references."));
                    }
                }
                else
                {
                    var unresRefStr = string.Join(", ", unresolvableExternalReferences.Distinct().Select((civicRef) => $"{civicRef.Type} \"{civicRef.Name}\""));
                    user.Player.Msg(new LocString($"Bundle has {unresolvableExternalReferences.Count} unresolvable external references: {unresRefStr}"));
                }
            }

        }

        #endregion
    }
}