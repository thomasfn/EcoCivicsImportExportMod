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
    using Gameplay.Civics.Immigration;
    using Gameplay.Objects;
    using Gameplay.Components;
    using Gameplay.Economy;
    using Gameplay.Settlements;
    using Gameplay.Systems;
    using Gameplay.Systems.Chat;
    using Eco.Shared.Utils;

    [ChatCommandHandler]
    public static class CivicsImpExpCommands
    {
        private static readonly IReadOnlyDictionary<string, Type> civicKeyToType = new Dictionary<string, Type>
        {
            {"law",                 typeof(Law) },
            {"electionprocess",     typeof(ElectionProcess) },
            {"electedtitle",        typeof(ElectedTitle) },
            {"appointedtitle",      typeof(AppointedTitle) },
            {"demographic",         typeof(Demographic) },
            {"constitution",        typeof(Constitution) },
            {"amendment",           typeof(ConstitutionalAmendment) },
            {"districtmap",         typeof(DistrictMap) },
            {"govaccount",          typeof(GovernmentBankAccount) },
            {"settlement",          typeof(Settlement) },
            {"immigrationpolicy",   typeof(ImmigrationPolicy) },
        };

        private static readonly IReadOnlyDictionary<Type, string> typeToCivicKey = new Dictionary<Type, string>(
            civicKeyToType
                .Select(pair => new KeyValuePair<Type, string>(pair.Value, pair.Key))
        );

        private static readonly IReadOnlyDictionary<string, ProposableState> stateNamesToStates = new Dictionary<string, ProposableState>
        {
            {"draft", ProposableState.Draft },
            {"proposed", ProposableState.Proposed },
            {"active", ProposableState.Active },
            {"removed", ProposableState.Removed },
        };

        private static bool TryGetTypeForCivicKey(IChatClient chatClient, string civicKey, out Type civicType)
        {
            if (civicKeyToType.TryGetValue(civicKey, out civicType)) { return true; }
            chatClient.Msg(new LocString($"Unknown civic key '{civicKey}' (expecting one of {string.Join(", ", civicKeyToType.Keys.Select(type => $"'{type}'"))})"));
            return false;
        }

        #region Exporting

        [ChatSubCommand("Civics", "Exports a civic object to a json file.", ChatAuthorizationLevel.Admin)]
        public static async Task Export(IChatClient chatClient, int id)
        {
            if (!UniversalIDs.TryGetByID(id, out IHasUniversalID obj) || obj == null)
            {
                chatClient.Msg(new LocString($"Failed to export object: none found by id {id}"));
                return;
            }
            if (!typeToCivicKey.TryGetValue(obj.GetType(), out var civicKey))
            {
                chatClient.Msg(new LocString($"Failed to export object: type {obj.GetType().Name} not supported by exporter"));
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
            if (!TryGetTypeForCivicKey(chatClient, civicKey, out var civicType)) { return (0, 0); }
            int successCount = 0, failCount = 0;
            foreach (var obj in Registrars.GetByDerivedType(civicType).All())
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

        private static int GetUsedSlotsCount(CivicObjectComponent civicObjectComponent, IDictionary<CivicObjectComponent, int> usedSlotsModifierDict = null)
        {
            int modifier;
            if (usedSlotsModifierDict == null || !usedSlotsModifierDict.TryGetValue(civicObjectComponent, out modifier)) { modifier = 0; }
            return civicObjectComponent.UsedSlots + modifier;
        }

        private static WorldObject FindFreeWorldObjectForCivic(Type civicType, Settlement settlement, IDictionary<CivicObjectComponent, int> usedSlotsModifierDict = null, Vector3? nearestTo = null)
        {
            var relevantWorldObjects = ServiceHolder<IWorldObjectManager>.Obj.All
                    .Where((worldObject) => worldObject.HasComponent<CivicObjectComponent>())
                    .Select((worldObject) => (worldObject, worldObject.GetComponent<CivicObjectComponent>()))
                    .Where((worldObjectAndComp) => worldObjectAndComp.Item2.ObjectType.IsAssignableFrom(civicType) && worldObjectAndComp.Item2.Settlement == settlement)
                    .Where((worldObjectAndComp) => GetUsedSlotsCount(worldObjectAndComp.Item2, usedSlotsModifierDict) < worldObjectAndComp.Item2.MaxCount);
            if (nearestTo != null)
            {
                relevantWorldObjects = relevantWorldObjects
                    .OrderBy((worldObjectAndComp) => World.WrappedDistance(worldObjectAndComp.worldObject.Position, nearestTo.Value));
            }
            return relevantWorldObjects.FirstOrDefault().worldObject;
        }

        private static int CountFreeSlotsForCivic(Type civicType, Settlement settlement)
        {
            return ServiceHolder<IWorldObjectManager>.Obj.All
                .Where((worldObject) => worldObject.HasComponent<CivicObjectComponent>())
                .Select((worldObject) => (worldObject, worldObject.GetComponent<CivicObjectComponent>()))
                .Where((worldObjectAndComp) => worldObjectAndComp.Item2.ObjectType.IsAssignableFrom(civicType) && worldObjectAndComp.Item2.Settlement == settlement)
                .Select((worldObjectAndComp) => worldObjectAndComp.Item2.MaxCount - worldObjectAndComp.Item2.UsedSlots)
                .Sum();
        }

        [ChatSubCommand("Civics", "Imports a civic object from a json file.", ChatAuthorizationLevel.Admin)]
        public static async Task Import(IChatClient chatClient, string source, Settlement targetSettlement = null)
        {
            // Check settlement
            if (FeatureConfig.Obj.SettlementEnabled && targetSettlement == null)
            {
                chatClient.Msg(Localizer.Do($"You must specify a settlement to import into!"));
                return;
            }
            targetSettlement ??= SettlementManager.Obj.LegacySettlement;

            // Import the bundle
            CivicBundle bundle;
            try
            {
                bundle = await Importer.ImportBundle(source);
            }
            catch (Exception ex)
            {
                chatClient.Msg(Localizer.Do($"Failed to import bundle: {ex.Message}"));
                Logger.Error($"Exception while importing from '{source}': {ex}");
                return;
            }

            // Determine settlement state
            var bundleSettlementCount = bundle.Civics.Count(c => c.Is<Settlement>());
            var bundleConstitutionCount = bundle.Civics.Count(c => c.Is<Constitution>());
            if (bundleSettlementCount > 1)
            {
                chatClient.Msg(Localizer.DoStr("Bundle contains more than 1 settlement, this is not allowed!"));
                return;
            }
            if (bundleConstitutionCount > 1)
            {
                chatClient.Msg(Localizer.DoStr("Bundle contains more than 1 constitution, this is not allowed!"));
                return;
            }
            if (!FeatureConfig.Obj.SettlementEnabled)
            {
                if (bundleSettlementCount > 0)
                {
                    chatClient.Msg(Localizer.DoStr("Bundle is not importable as it contains a settlement and the settlement system is not enabled."));
                    return;
                }
                if (bundleConstitutionCount > 0)
                {
                    chatClient.Msg(Localizer.DoStr("Bundle is not importable as it contains a constitution and the settlement system is not enabled."));
                    return;
                }
            }

            // Fetch settlement overwrite civics
            var settlementCivicRefs = new HashSet<CivicReference>();
            var settlementCivics = new HashSet<IHasID>();
            var settlementBundledCivic = bundle.Settlement;
            if (FeatureConfig.Obj.SettlementEnabled && settlementBundledCivic.HasValue)
            {
                var settlementOverwriteCivics = bundle.GetSettlementOverwriteCivics(targetSettlement);
                foreach (var pair in settlementOverwriteCivics)
                {
                    settlementCivicRefs.Add(pair.Key);
                    settlementCivics.Add(pair.Value);
                }
                // TODO: Do we want to popup a notice saying what we're going to do, as this could be a destructive operation?
            }

            // Check that there are enough free slots for all the civics in the bundle
            var bundledCivicsByType = bundle.Civics
                .Where((bundledCivic) => !settlementCivicRefs.Contains(bundledCivic.AsReference))
                .GroupBy((bundledCivic) => bundledCivic.Type)
                .Where((grouping) => typeof(IProposable).IsAssignableFrom(grouping.Key));
            foreach (var grouping in bundledCivicsByType)
            {
                var freeSlots = CountFreeSlotsForCivic(grouping.Key, targetSettlement);
                int importCount = grouping.Count();
                if (importCount > freeSlots)
                {
                    chatClient.Msg(Localizer.Do($"Unable to import {importCount} of {grouping.Key.Name} (only {freeSlots} available slots for this civic type)"));
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
                chatClient.Msg(Localizer.Do($"Failed to perform migrations on civic: {ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }
            if (migrationReport.Any())
            {
                chatClient.Msg(Localizer.Do($"Some migrations were performed:\n{string.Join("\n", migrationReport)}"));
            }

            // Import the objects from the bundle
            IEnumerable<IHasID> importedObjects;
            try
            {
                importedObjects = bundle.ImportAll(targetSettlement, chatClient as User);
            }
            catch (Exception ex)
            {
                chatClient.Msg(Localizer.Do($"Failed to import civic: {ex.Message}"));
                return;
            }
            CivicsImpExpPlugin.Obj.LastImport.Clear();
            if (!settlementCivicRefs.Any())
            {
                CivicsImpExpPlugin.Obj.LastImport.AddRange(importedObjects);
            }

            // Notify of import for non-proposables (e.g. bank accounts, appointed titles)
            var importReport = new List<string>();
            foreach (var obj in importedObjects.Where((obj) => obj is not IProposable && obj is not IParentedEntry))
            {
                if (obj is not ILinkable linkable) { continue; }
                importReport.Add(linkable.UILink());
            }
            if (importReport.Count > 0)
            {
                chatClient.Msg(Localizer.Do($"Imported {string.Join(", ", importReport)} from '{source}'"));
            }
            if (settlementCivicRefs.Any())
            {
                chatClient.Msg(Localizer.DoStr($"This operation is not undoable."));
            }

            // Slot each civic into the relevant world object
            IDictionary<CivicObjectComponent, int> usedSlotsModifierDict = new Dictionary<CivicObjectComponent, int>();
            int importProposableCount = 0;
            foreach (var obj in importedObjects.Where((obj) => obj is IProposable && obj is not IParentedEntry))
            {
                ++importProposableCount;
                var proposable = obj as IProposable;
                if (obj == targetSettlement)
                {
                    chatClient.Msg(Localizer.Do($"Imported {proposable.UILink()} from '{source}'"));
                    continue;
                }
                if (settlementCivics.Contains(obj))
                {
                    chatClient.Msg(Localizer.Do($"Imported {proposable.UILink()} from '{source}' as core civic of {targetSettlement.UILink()}"));
                    continue;
                }
                var user = chatClient as User;
                var worldObject = FindFreeWorldObjectForCivic(obj.GetType(), targetSettlement, usedSlotsModifierDict, user?.Position);
                if (worldObject == null)
                {
                    // This should never happen as we already checked above for free slots and early'd out, but just in case...
                    if (!settlementCivicRefs.Any()) { Importer.Cleanup(importedObjects); }
                    chatClient.Msg(Localizer.Do($"Failed to import civic of type '{obj.GetType().Name}': no world objects found with available space for the civic"));
                    CivicsImpExpPlugin.Obj.LastImport.Clear();
                    return;
                }
                var civicObjectComponent = worldObject.GetComponent<CivicObjectComponent>();

                proposable.AssignHostObject(worldObject);
                if (usedSlotsModifierDict.TryGetValue(civicObjectComponent, out int currentModifier))
                {
                    usedSlotsModifierDict[civicObjectComponent] = currentModifier + 1;
                }
                else
                {
                    usedSlotsModifierDict.Add(civicObjectComponent, 1);
                }
                chatClient.Msg(Localizer.Do($"Imported {proposable.UILink()} from '{source}' onto {worldObject.UILink()}"));
            }

            // Sanity check
            if (importProposableCount == 0 && importReport.Count == 0)
            {
                chatClient.Msg(Localizer.DoStr($"Nothing imported - was the bundle empty or corrupt?"));
            }
        }

        [ChatSubCommand("Civics", "Undoes the last imported civic bundle. Use with extreme care.", ChatAuthorizationLevel.Admin)]
        public static void UndoImport(IChatClient chatClient)
        {
            Importer.Cleanup(CivicsImpExpPlugin.Obj.LastImport);
            chatClient.Msg(Localizer.Do($"Deleted {CivicsImpExpPlugin.Obj.LastImport.Count} objects from the last import"));
            CivicsImpExpPlugin.Obj.LastImport.Clear();
        }

        [ChatSubCommand("Civics", "Prints details about a civic bundle without actually importing anything.", ChatAuthorizationLevel.Admin)]
        public static async Task BundleInfo(IChatClient chatClient, string source, Settlement targetSettlement = null)
        {
            // Check settlement
            if (FeatureConfig.Obj.SettlementEnabled && targetSettlement == null)
            {
                chatClient.Msg(Localizer.DoStr("You must specify a settlement to import into!"));
                return;
            }
            targetSettlement ??= SettlementManager.Obj.LegacySettlement;

            // Import the bundle
            CivicBundle bundle;
            try
            {
                bundle = await Importer.ImportBundle(source);
            }
            catch (Exception ex)
            {
                chatClient.Msg(Localizer.Do($"Failed to import bundle: {ex.Message}"));
                Logger.Error(ex.ToString());
                return;
            }

            // Determine settlement state
            var bundleSettlementCount = bundle.Civics.Count(c => c.Is<Settlement>());
            if (bundleSettlementCount > 1)
            {
                chatClient.Msg(Localizer.DoStr("Bundle contains more than 1 settlement, this is not allowed!"));
                return;
            }
            if (!FeatureConfig.Obj.SettlementEnabled && bundleSettlementCount > 0)
            {
                chatClient.Msg(Localizer.DoStr("Bundle is not importable as it contains a settlement and the settlement system is not enabled."));
                return;
            }

            // Print settlement overwrite civics
            var settlementCivics = new HashSet<CivicReference>();
            var settlementBundledCivic = bundle.Settlement;
            if (FeatureConfig.Obj.SettlementEnabled && settlementBundledCivic.HasValue)
            {
                var settlementOverwriteCivics = bundle.GetSettlementOverwriteCivics(targetSettlement);
                settlementCivics.Add(settlementBundledCivic.Value.AsReference);
                chatClient.Msg(Localizer.Do($"Bundle contains a settlement. {targetSettlement.UILink()} will be replaced by '{settlementBundledCivic.Value.Name}'."));
                foreach (var pair in settlementOverwriteCivics)
                {
                    if (pair.Value is ILinkable linkable)
                    {
                        chatClient.Msg(Localizer.Do($"- {linkable.UILink()} will be replaced by '{pair.Key.Name}'."));
                    }
                    else if (pair.Value is ILinkable)
                    {
                        chatClient.Msg(Localizer.Do($"- {pair.Value.MarkedUpName} will be replaced by '{pair.Key.Name}'."));
                    }
                    settlementCivics.Add(pair.Key);
                }
            }

            // Print type metrics
            var bundledCivicsByType = bundle.Civics
                .Where((bundledCivic) => !settlementCivics.Contains(bundledCivic.AsReference))
                .GroupBy((bundledCivic) => bundledCivic.Type)
                .Where((grouping) => typeof(IProposable).IsAssignableFrom(grouping.Key));
            foreach (var grouping in bundledCivicsByType)
            {
                var freeSlots = CountFreeSlotsForCivic(grouping.Key, targetSettlement);
                int importCount = grouping.Count();
                chatClient.Msg(Localizer.Do($"Bundle has {importCount} of {grouping.Key.Name} (there are {freeSlots} available slots for this civic type)"));
                var subobjectsByType = grouping
                    .SelectMany((bundledCivic) => bundledCivic.InlineObjects)
                    .GroupBy((bundledCivic) => bundledCivic.Type);
                for (int i = 0, l = subobjectsByType.Count(); i < l; ++i)
                {
                    var subGrouping = subobjectsByType.Skip(i).First();
                    chatClient.Msg(Localizer.Do($" - with {importCount} of {subGrouping.Key.Name}"));
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
                chatClient.Msg(Localizer.Do($"Bundle has no external references."));
            }
            else
            {
                if (resolvableExternalReferences.Count > 0)
                {
                    var resRefStr = string.Join(", ", resolvableExternalReferences.Distinct().Select((obj) => obj is ILinkable linkable ? linkable.UILink().ToString() : obj.ToString()));
                    chatClient.Msg(Localizer.Do($"Bundle has {resolvableExternalReferences.Count} references to the following: {resRefStr}"));
                    if (unresolvableExternalReferences.Count > 0)
                    {
                        var unresRefStr = string.Join(", ", unresolvableExternalReferences.Distinct().Select((civicRef) => $"{civicRef.Type} \"{civicRef.Name}\""));
                        chatClient.Msg(Localizer.Do($"Bundle has {unresolvableExternalReferences.Count} unresolvable external references: {unresRefStr}"));
                    }
                    else
                    {
                        chatClient.Msg(Localizer.Do($"Bundle has no unresolvable external references."));
                    }
                }
                else
                {
                    var unresRefStr = string.Join(", ", unresolvableExternalReferences.Distinct().Select((civicRef) => $"{civicRef.Type} \"{civicRef.Name}\""));
                    chatClient.Msg(Localizer.Do($"Bundle has {unresolvableExternalReferences.Count} unresolvable external references: {unresRefStr}"));
                }
            }

        }

        #endregion

        [ChatSubCommand("Civics", "Fixes all non-removed civics with missing creators.", ChatAuthorizationLevel.Admin)]
        public static void FixMissingCreators(IChatClient chatClient)
        {
            if (chatClient is not User user)
            {
                chatClient.Msg(Localizer.Do($"Must be a valid user, RCON is not supported"));
                return;
            }
            IList<(Type, int)> fixCounts = new List<(Type, int)>();
            foreach (var civicType in typeToCivicKey.Keys)
            {
                var registrar = Registrars.GetByDerivedType(civicType);
                int localNumFixed = 0;
                foreach (var civicObj in registrar.All())
                {
                    if (civicObj is not IProposable proposable) { continue; }
                    if (proposable.State == ProposableState.Uninitialized || proposable.State == ProposableState.Removed) { continue; }
                    if (proposable.Creator == null)
                    {
                        proposable.Creator = user;
                        ++localNumFixed;
                    }
                }
                if (localNumFixed > 0)
                {
                    registrar.Save();
                    fixCounts.Add((civicType, localNumFixed));
                }
            }
            if (fixCounts.Count == 0)
            {
                chatClient.Msg(Localizer.Do($"No civics with missing creators were found"));
                return;
            }
            chatClient.Msg(Localizer.Do($"Found the following civics and corrected their owner to {user.MarkedUpName}: {string.Join(", ", fixCounts.Select(x => $"{x.Item2} of {x.Item1.Name}"))}"));
        }
    }
}