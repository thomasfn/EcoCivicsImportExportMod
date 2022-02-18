using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Plugins.Interfaces;
    using Core.Utils;
    using Core.Systems;

    using Shared.Localization;
    using Shared.Math;
    using Shared.IoC;
    using Shared.Items;

    using Gameplay.Players;
    using Gameplay.Systems.Messaging.Chat.Commands;
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
    using Gameplay.Economy;
    

    public struct RawUser
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class CivicsImpExpPlugin : IModKitPlugin, IInitializablePlugin, IChatCommandHandler
    {
        private static IReadOnlyDictionary<string, Type> civicKeyToType;
        private static IReadOnlyDictionary<string, IRegistrar> civicKeyToRegistrar;
        private static IReadOnlyDictionary<string, ProposableState> stateNamesToStates;

        public const string ImportExportDirectory = "civics";

        private static readonly List<IHasID> lastImport = new List<IHasID>();

        public string GetStatus()
        {
            return "Idle";
        }

        public void Initialize(TimedTask timer)
        {
            civicKeyToType = new Dictionary<string, Type>
            {
                {"law",             typeof(Law) },
                {"electionprocess", typeof(ElectionProcess) },
                {"electedtitle",    typeof(ElectedTitle) },
                {"appointedtitle",  typeof(AppointedTitle) },
                {"demographic",     typeof(Demographic) },
                {"constitution",    typeof(Constitution) },
                {"amendment",       typeof(ConstitutionalAmendment) },
                {"districtmap",     typeof(DistrictMap) },
                {"govaccount",      typeof(GovernmentBankAccount) },
            };
            civicKeyToRegistrar = new Dictionary<string, IRegistrar>(civicKeyToType.Select((kv) => new KeyValuePair<string, IRegistrar>(kv.Key, Registrars.GetByDerivedType(kv.Value))));
            stateNamesToStates = new Dictionary<string, ProposableState>
            {
                {"draft", ProposableState.Draft },
                {"proposed", ProposableState.Proposed },
                {"active", ProposableState.Active },
                {"removed", ProposableState.Removed },
            };
            Directory.CreateDirectory(ImportExportDirectory);
            Logger.Info("Initialized and ready to go");
        }

        private static bool TryGetRegistrarForCivicKey(User user, string civicKey, out Type civicType, out IRegistrar registrar)
        {
            if (civicKeyToType.TryGetValue(civicKey, out civicType) && civicKeyToRegistrar.TryGetValue(civicKey, out registrar)) { return true; }
            user.Player.Msg(new LocString($"Unknown civic key '{civicKey}' (expecting one of {string.Join(", ", civicKeyToRegistrar.Keys.Select(type => $"'{type}'"))})"));
            registrar = null;
            return false;
        }

        #region Exporting

        [ChatSubCommand("Civics", "Exports a civic object to a json file.", ChatAuthorizationLevel.Admin)]
        public static void Export(User user, string civicKey, int id)
        {
            if (!TryGetRegistrarForCivicKey(user, civicKey, out var civicType, out var registrar)) { return; }
            var obj = registrar.GetById(id);
            if (obj == null || !civicType.IsAssignableFrom(obj.GetType()))
            {
                user.Player.Msg(new LocString($"Failed to export {civicKey}: none found by id {id}"));
                return;
            }
            string outPath = Path.Combine("civics", $"{civicKey}-{id}.json");
            try
            {
                Exporter.Export(obj, outPath);
            }
            catch (Exception ex)
            {
                user.Player.Msg(new LocString($"Failed to export {civicKey}: {ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }
            user.Player.Msg(new LocString($"Exported {civicKey} {id} to '{outPath}'"));
        }

        private static void ExportAllOfInternal(User user, string civicKey, ProposableState? stateFilter, ref int successCount, ref int failCount)
        {
            if (!TryGetRegistrarForCivicKey(user, civicKey, out var civicType, out var registrar)) { return; }
            foreach (var obj in registrar.All())
            {
                if (!civicType.IsAssignableFrom(obj.GetType())) { continue; }
                if (stateFilter != null && obj is IProposable proposable && proposable.State != stateFilter.Value) { continue; }
                string outPath = Path.Combine(ImportExportDirectory, $"{civicKey}-{obj.Id}.json");
                try
                {
                    Exporter.Export(obj, outPath);
                    ++successCount;
                }
                catch (Exception ex)
                {
                    user.Player.Msg(new LocString($"Failed to export {civicKey} {obj.Id}: {ex.Message}"));
                    Logger.Error(ex.ToString());
                    ++failCount;
                }
            }
        }

        [ChatSubCommand("Civics", "Exports all civic objects of a kind to json files.", ChatAuthorizationLevel.Admin)]
        public static void ExportAllOf(User user, string civicKey, string onlyThisState = "")
        {
            int successCount = 0, failCount = 0;
            ProposableState? stateFilter = null;
            if (!string.IsNullOrEmpty(onlyThisState))
            {
                if (!stateNamesToStates.TryGetValue(onlyThisState, out var rawStateFilter))
                {
                    user.Player.Msg(new LocString($"Invalid civic state '{onlyThisState}'"));
                    return;
                }
                stateFilter = rawStateFilter;
            }
            ExportAllOfInternal(user, civicKey, stateFilter, ref successCount, ref failCount);
            if (failCount > 0)
            {
                if (successCount == 0)
                {
                    user.Player.Msg(new LocString($"Failed to export all {failCount} of {civicKey}"));
                }
                else
                {
                    user.Player.Msg(new LocString($"successfully exported {successCount} of {civicKey}, but failed to export {failCount} of {civicKey}"));
                }
            }
            else
            {
                user.Player.Msg(new LocString($"successfully exported all {successCount} of {civicKey}"));
            }
        }

        [ChatSubCommand("Civics", "Exports all civic objects to json files.", ChatAuthorizationLevel.Admin)]
        public static void ExportAll(User user, string onlyThisState = "")
        {
            ProposableState? stateFilter = null;
            if (!string.IsNullOrEmpty(onlyThisState))
            {
                if (!stateNamesToStates.TryGetValue(onlyThisState, out var rawStateFilter))
                {
                    user.Player.Msg(new LocString($"Invalid civic state '{onlyThisState}'"));
                    return;
                }
                stateFilter = rawStateFilter;
            }
            int successCount = 0, failCount = 0;
            foreach (var civicKey in civicKeyToRegistrar.Keys)
            {
                ExportAllOfInternal(user, civicKey, stateFilter, ref successCount, ref failCount);
            }
            if (failCount > 0)
            {
                if (successCount == 0)
                {
                    user.Player.Msg(new LocString($"Failed to export all {failCount} civic objects"));
                }
                else
                {
                    user.Player.Msg(new LocString($"successfully exported {successCount} civic objects, but failed to export {failCount} of civic objects"));
                }
            }
            else
            {
                user.Player.Msg(new LocString($"successfully exported all {successCount} civic objects"));
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

        private static WorldObject FindFreeWorldObjectForCivic(Type civicType, IDictionary<CivicObjectComponent, int> usedSlotsModifierDict = null, Vector3? nearestTo = null)
        {
            var worldObjectManager = ServiceHolder<IWorldObjectManager>.Obj;
            var relevantWorldObjects = worldObjectManager.All
                .Where((worldObject) => worldObject.HasComponent<CivicObjectComponent>())
                .Select((worldObject) => (worldObject, worldObject.GetComponent<CivicObjectComponent>()))
                .Where((worldObjectAndComp) => worldObjectAndComp.Item2.ObjectType.IsAssignableFrom(civicType))
                .Where((worldObjectAndComp) => GetUsedSlots(worldObjectAndComp.Item2, usedSlotsModifierDict) < worldObjectAndComp.Item2.MaxCount);
            if (nearestTo != null)
            {
                relevantWorldObjects = relevantWorldObjects
                    .OrderBy((worldObjectAndComp) => worldObjectAndComp.worldObject.Position.WrappedDistance(nearestTo.Value));
            }
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
                Logger.Error($"Exception while importing from '{source}': {ex}");
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

            // Perform migrations
            IEnumerable<string> migrationReport;
            try
            {
                migrationReport = bundle.ApplyMigrations();
            }
            catch (Exception ex)
            {
                user.Player.Msg(new LocString($"Failed to perform migrations on civic: {ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }
            if (migrationReport.Count() > 0)
            {
                user.Player.Msg(new LocString($"Some migrations were performed:\n{string.Join("\n", migrationReport)}"));
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
                return;
            }
            lastImport.Clear();
            lastImport.AddRange(importedObjects);

            // Notify of import for non-proposables (e.g. bank accounts, appointed titles)
            foreach (var obj in importedObjects.Where((obj) => !(obj is IProposable) && !(obj is IParentedEntry)))
            {
                var linkable = obj as ILinkable;
                if (linkable == null) { continue; }
                user.Player.Msg(new LocString($"Imported {linkable.UILink()} from '{source}'"));
            }

            // Slot each civic into the relevant world object
            int numCivics = 0;
            IDictionary<CivicObjectComponent, int> usedSlotsModifierDict = new Dictionary<CivicObjectComponent, int>();
            foreach (var obj in importedObjects.Where((obj) => obj is IProposable && !(obj is IParentedEntry)))
            {
                var worldObject = FindFreeWorldObjectForCivic(obj.GetType(), usedSlotsModifierDict, user.Player?.Position);
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