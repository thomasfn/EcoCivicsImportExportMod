using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;

    using Shared.Localization;
    using Shared.IoC;
    using Shared.Items;
    using Shared.Voxel;

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
    using Gameplay.Settlements;
    using Gameplay.Systems;
    using Gameplay.Systems.Chat;

    [ChatCommandHandler]
    public static class CivicsImpExpCommands
    {
        private static IReadOnlyDictionary<string, Type> civicKeyToType = new Dictionary<string, Type>
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

        private static IReadOnlyDictionary<string, ProposableState> stateNamesToStates = new Dictionary<string, ProposableState>
        {
            {"draft", ProposableState.Draft },
            {"proposed", ProposableState.Proposed },
            {"active", ProposableState.Active },
            {"removed", ProposableState.Removed },
        };

        private static bool TryGetRegistrarForCivicKey(IChatClient chatClient, string civicKey, out Type civicType, out IRegistrar registrar)
        {
            if (civicKeyToType.TryGetValue(civicKey, out civicType))
            {
                registrar = Registrars.GetByDerivedType(civicType);
                return true;
            }
            chatClient.Msg(new LocString($"Unknown civic key '{civicKey}' (expecting one of {string.Join(", ", civicKeyToType.Keys.Select(type => $"'{type}'"))})"));
            registrar = null;
            return false;
        }

        #region Exporting

        [ChatSubCommand("Civics", "Exports a civic object to a json file.", ChatAuthorizationLevel.Admin)]
        public static async Task Export(IChatClient chatClient, string civicKey, int id)
        {
            if (!TryGetRegistrarForCivicKey(chatClient, civicKey, out var civicType, out var registrar)) { return; }
            var obj = registrar.GetById(id);
            if (obj == null || !civicType.IsAssignableFrom(obj.GetType()))
            {
                chatClient.Msg(new LocString($"Failed to export {civicKey}: none found by id {id}"));
                return;
            }
            string outPath = Path.Combine("civics", $"{civicKey}-{id}.json");
            try
            {
                await Exporter.Export(obj, outPath);
            }
            catch (Exception ex)
            {
                chatClient.Msg(new LocString($"Failed to export {civicKey}: {ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }
            chatClient.Msg(new LocString($"Exported {civicKey} {id} to '{outPath}'"));
        }

        private static async Task<(int successCount, int failCount)> ExportAllOfInternal(IChatClient chatClient, string civicKey, ProposableState? stateFilter)
        {
            if (!TryGetRegistrarForCivicKey(chatClient, civicKey, out var civicType, out var registrar)) { return (0, 0); }
            int successCount = 0, failCount = 0;
            foreach (var obj in registrar.All())
            {
                if (!civicType.IsAssignableFrom(obj.GetType())) { continue; }
                if (stateFilter != null && obj is IProposable proposable && proposable.State != stateFilter.Value) { continue; }
                string outPath = Path.Combine(CivicsImpExpPlugin.ImportExportDirectory, $"{civicKey}-{obj.Id}.json");
                try
                {
                    await Exporter.Export(obj, outPath);
                    ++successCount;
                }
                catch (Exception ex)
                {
                    chatClient.Msg(new LocString($"Failed to export {civicKey} {obj.Id}: {ex.Message}"));
                    Logger.Error(ex.ToString());
                    ++failCount;
                }
            }
            return (successCount, failCount);
        }

        [ChatSubCommand("Civics", "Exports all civic objects of a kind to json files.", ChatAuthorizationLevel.Admin)]
        public static async Task ExportAllOf(IChatClient chatClient, string civicKey, string onlyThisState = "")
        {
            ProposableState? stateFilter = null;
            if (!string.IsNullOrEmpty(onlyThisState))
            {
                if (!stateNamesToStates.TryGetValue(onlyThisState, out var rawStateFilter))
                {
                    chatClient.Msg(new LocString($"Invalid civic state '{onlyThisState}'"));
                    return;
                }
                stateFilter = rawStateFilter;
            }
            var (successCount, failCount) = await ExportAllOfInternal(chatClient, civicKey, stateFilter);
            if (failCount > 0)
            {
                if (successCount == 0)
                {
                    chatClient.Msg(new LocString($"Failed to export all {failCount} of {civicKey}"));
                }
                else
                {
                    chatClient.Msg(new LocString($"successfully exported {successCount} of {civicKey}, but failed to export {failCount} of {civicKey}"));
                }
            }
            else
            {
                chatClient.Msg(new LocString($"successfully exported all {successCount} of {civicKey}"));
            }
        }

        [ChatSubCommand("Civics", "Exports all civic objects to json files.", ChatAuthorizationLevel.Admin)]
        public static async Task ExportAll(IChatClient clientClient, string onlyThisState = "")
        {
            ProposableState? stateFilter = null;
            if (!string.IsNullOrEmpty(onlyThisState))
            {
                if (!stateNamesToStates.TryGetValue(onlyThisState, out var rawStateFilter))
                {
                    clientClient.Msg(new LocString($"Invalid civic state '{onlyThisState}'"));
                    return;
                }
                stateFilter = rawStateFilter;
            }
            int allSuccessCount = 0, allFailCount = 0;
            foreach (var civicKey in civicKeyToType.Keys)
            {
                var (successCount, failCount) = await ExportAllOfInternal(clientClient, civicKey, stateFilter);
                allSuccessCount += successCount;
                allFailCount += failCount;
            }
            if (allFailCount > 0)
            {
                if (allSuccessCount == 0)
                {
                    clientClient.Msg(new LocString($"Failed to export all {allFailCount} civic objects"));
                }
                else
                {
                    clientClient.Msg(new LocString($"successfully exported {allSuccessCount} civic objects, but failed to export {allFailCount} of civic objects"));
                }
            }
            else
            {
                clientClient.Msg(new LocString($"successfully exported all {allSuccessCount} civic objects"));
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
                    .OrderBy((worldObjectAndComp) => World.WrappedDistance(worldObjectAndComp.worldObject.Position, nearestTo.Value));
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
        public static async Task Import(IChatClient chatClient, string source, Settlement targetSettlement = null)
        {
            // Check settlement
            if (FeatureConfig.Obj.SettlementSystemEnabled && targetSettlement == null)
            {
                chatClient.Msg(new LocString($"You must specify a settlement to import into!"));
                return;
            }

            // Import the bundle
            CivicBundle bundle;
            try
            {
                bundle = await Importer.ImportBundle(source);
            }
            catch (Exception ex)
            {
                chatClient.Msg(new LocString($"Failed to import bundle: {ex.Message}"));
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
                    chatClient.Msg(new LocString($"Unable to import {importCount} of {grouping.Key.Name} (only {freeSlots} available slots for this civic type)"));
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
                chatClient.Msg(new LocString($"Failed to perform migrations on civic: {ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }
            if (migrationReport.Count() > 0)
            {
                chatClient.Msg(new LocString($"Some migrations were performed:\n{string.Join("\n", migrationReport)}"));
            }

            // Import the objects from the bundle
            IEnumerable<IHasID> importedObjects;
            try
            {
                importedObjects = bundle.ImportAll(targetSettlement ?? SettlementManager.Obj.LegacySettlement);
            }
            catch (Exception ex)
            {
                chatClient.Msg(new LocString($"Failed to import civic: {ex.Message}"));
                return;
            }
            CivicsImpExpPlugin.Obj.LastImport.Clear();
            CivicsImpExpPlugin.Obj.LastImport.AddRange(importedObjects);

            // Notify of import for non-proposables (e.g. bank accounts, appointed titles)
            foreach (var obj in importedObjects.Where((obj) => !(obj is IProposable) && !(obj is IParentedEntry)))
            {
                var linkable = obj as ILinkable;
                if (linkable == null) { continue; }
                chatClient.Msg(new LocString($"Imported {linkable.UILink()} from '{source}'"));
            }

            // Slot each civic into the relevant world object
            int numCivics = 0;
            IDictionary<CivicObjectComponent, int> usedSlotsModifierDict = new Dictionary<CivicObjectComponent, int>();
            foreach (var obj in importedObjects.Where((obj) => obj is IProposable && !(obj is IParentedEntry)))
            {
                var player = chatClient is User user ? user.Player : null;
                var worldObject = FindFreeWorldObjectForCivic(obj.GetType(), usedSlotsModifierDict, player?.Position);
                if (worldObject == null)
                {
                    // This should never happen as we already checked above for free slots and early'd out, but just in case...
                    Importer.Cleanup(importedObjects);
                    chatClient.Msg(new LocString($"Failed to import civic of type '{obj.GetType().Name}': no world objects found with available space for the civic"));
                    CivicsImpExpPlugin.Obj.LastImport.Clear();
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
                chatClient.Msg(new LocString($"Imported {proposable.UILink()} from '{source}' onto {worldObject.UILink()}"));
                ++numCivics;
            }
        }

        [ChatSubCommand("Civics", "Undoes the last imported civic bundle. Use with extreme care.", ChatAuthorizationLevel.Admin)]
        public static void UndoImport(IChatClient chatClient)
        {
            Importer.Cleanup(CivicsImpExpPlugin.Obj.LastImport);
            chatClient.Msg(new LocString($"Deleted {CivicsImpExpPlugin.Obj.LastImport.Count} objects from the last import"));
            CivicsImpExpPlugin.Obj.LastImport.Clear();
        }

        [ChatSubCommand("Civics", "Prints details about a civic bundle without actually importing anything.", ChatAuthorizationLevel.Admin)]
        public static async Task BundleInfo(IChatClient chatClient, string source)
        {
            // Import the bundle
            CivicBundle bundle;
            try
            {
                bundle = await Importer.ImportBundle(source);
            }
            catch (Exception ex)
            {
                chatClient.Msg(new LocString($"Failed to import bundle: {ex.Message}"));
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
                chatClient.Msg(new LocString($"Bundle has {importCount} of {grouping.Key.Name} (there are {freeSlots} available slots for this civic type)"));
                var subobjectsByType = grouping
                    .SelectMany((bundledCivic) => bundledCivic.InlineObjects)
                    .GroupBy((bundledCivic) => bundledCivic.Type);
                for (int i = 0, l = subobjectsByType.Count(); i < l; ++i)
                {
                    var subGrouping = subobjectsByType.Skip(i).First();
                    chatClient.Msg(new LocString($" - with {importCount} of {subGrouping.Key.Name}"));
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
                chatClient.Msg(new LocString($"Bundle has no external references."));
            }
            else
            {
                if (resolvableExternalReferences.Count > 0)
                {
                    var resRefStr = string.Join(", ", resolvableExternalReferences.Distinct().Select((obj) => obj is ILinkable linkable ? linkable.UILink().ToString() : obj.ToString()));
                    chatClient.Msg(new LocString($"Bundle has {resolvableExternalReferences.Count} references to the following: {resRefStr}"));
                    if (unresolvableExternalReferences.Count > 0)
                    {
                        var unresRefStr = string.Join(", ", unresolvableExternalReferences.Distinct().Select((civicRef) => $"{civicRef.Type} \"{civicRef.Name}\""));
                        chatClient.Msg(new LocString($"Bundle has {unresolvableExternalReferences.Count} unresolvable external references: {unresRefStr}"));
                    }
                    else
                    {
                        chatClient.Msg(new LocString($"Bundle has no unresolvable external references."));
                    }
                }
                else
                {
                    var unresRefStr = string.Join(", ", unresolvableExternalReferences.Distinct().Select((civicRef) => $"{civicRef.Type} \"{civicRef.Name}\""));
                    chatClient.Msg(new LocString($"Bundle has {unresolvableExternalReferences.Count} unresolvable external references: {unresRefStr}"));
                }
            }

        }

        #endregion
    }
}