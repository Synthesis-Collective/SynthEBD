using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SynthEBD
{  
    public class PatcherEnvironmentProvider
    {
        public static PatcherEnvironmentProvider Instance = new();

        private static Lazy<PatcherEnvironment> _env = new(
            () =>
            {
                if (PatcherSettings.General != null)
                {
                    return new PatcherEnvironment(PatcherSettings.General.SkyrimVersion);
                }
                else
                {
                    return new PatcherEnvironment(SkyrimRelease.SkyrimSE);
                }
                
            });

        public static PatcherEnvironment Environment => _env.Value;
    }

    public class PatcherEnvironment : IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter>
    {
        private bool disposedValue;

        public ILoadOrder<IModListing<ISkyrimModGetter>> LoadOrder { get; private set; }
        public MutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache { get; private set; }

        public DirectoryPath DataFolderPath { get; private set; }

        public GameRelease GameRelease { get; private set; }

        public FilePath LoadOrderFilePath { get; private set; }

        public FilePath? CreationClubListingsFilePath { get; private set; }

        ILinkCache<ISkyrimMod, ISkyrimModGetter> IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter>.LinkCache => LinkCache;

        ILoadOrderGetter<IModListingGetter<IModGetter>> IGameEnvironmentState.LoadOrder => LoadOrder;

        ILinkCache IGameEnvironmentState.LinkCache => LinkCache;

        private static IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> OriginState { get; set; }

        public PatcherEnvironment(SkyrimRelease gameType)
        {
            try
            {
                OriginState = GameEnvironment.Typical.Skyrim(gameType, LinkCachePreferences.OnlyIdentifiers());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Patcher environment creation failed with the following error: " + Environment.NewLine + ex.Message);
                Environment.Exit(-1);
            }

            if (PatcherSettings.General != null)
            {
                LoadOrder = (ILoadOrder<IModListing<ISkyrimModGetter>>)OriginState.LoadOrder.ListedOrder
                    .OnlyEnabledAndExisting()
                    .RemoveModAndDependents(PatcherSettings.General.PatchFileName + ".esp", false) // remove the output plugin and any plugins mastered to previous versions of it
                    .ToLoadOrder();
            }
            else
            {
                LoadOrder = OriginState.LoadOrder;
            }
            LinkCache = LoadOrder.ToMutableLinkCache();

            DataFolderPath = OriginState.DataFolderPath;
            LoadOrderFilePath = OriginState.LoadOrderFilePath;
            CreationClubListingsFilePath = OriginState.CreationClubListingsFilePath;
        }
        public void Refresh(string outputModName, bool verbose)
        {
            if (PatcherSettings.General != null)
            {
                LoadOrder = (ILoadOrder<IModListing<ISkyrimModGetter>>)OriginState.LoadOrder.ListedOrder
                    .OnlyEnabledAndExisting()
                    .RemoveModAndDependents(outputModName + ".esp", verbose) // remove the output plugin and any plugins mastered to previous versions of it
                    .ToLoadOrder();
            }
            else
            {
                LoadOrder = OriginState.LoadOrder;
            }
            // Note: Do not rebuild the linkcache from the new load order. Not necessary, and breaks FormKey picker UIs
        }

        public void Refresh(bool verbose)
        {
            if (PatcherSettings.General != null)
            {
                LoadOrder = (ILoadOrder<IModListing<ISkyrimModGetter>>)OriginState.LoadOrder.ListedOrder
                    .OnlyEnabledAndExisting()
                    .RemoveModAndDependents(PatcherSettings.General.PatchFileName + ".esp", verbose) // remove the output plugin and any plugins mastered to previous versions of it
                    .ToLoadOrder();
            }
            else
            {
                LoadOrder = OriginState.LoadOrder;
            }
            // Note: Do not rebuild the linkcache from the new load order. Not necessary, and breaks FormKey picker UIs
        }

        public void RefreshAndChangeGameType(SkyrimRelease gameType, string outputModName)
        {
            try
            {
                OriginState = GameEnvironment.Typical.Skyrim(gameType, LinkCachePreferences.OnlyIdentifiers());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Patcher environment creation failed with the following error: " + Environment.NewLine + ex.Message);
                Environment.Exit(-1);
            }
            Refresh(outputModName, false);
            LinkCache = LoadOrder.ToMutableLinkCache();
            DataFolderPath = OriginState.DataFolderPath;
            LoadOrderFilePath = OriginState.LoadOrderFilePath;
            CreationClubListingsFilePath = OriginState.CreationClubListingsFilePath;
        }

        public void SuspendEnvironment()
        {
            if (OriginState != null)
            {
                OriginState.LoadOrder.Dispose(); // release outputMod.esp so that it can be written to even if it's in the load order
            }
        }
        public void ResumeEnvironment()
        {
            OriginState = GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE, LinkCachePreferences.OnlyIdentifiers());
            Refresh(false);
        }

        public virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~PatcherEnvironment()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        void IDisposable.Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        // ...
    }

    public static class LoadOrderExtensions
    {
        public static IEnumerable<IModListing<ISkyrimModGetter>> RemoveModAndDependents(this IEnumerable<IModListing<ISkyrimModGetter>> listedOrder, string outputModName, bool verbose)
        {
            List<IModListing<ISkyrimModGetter>> filteredLoadOrder = new List<IModListing<ISkyrimModGetter>>();
            HashSet<string> removedMods = new HashSet<string>();
            foreach (var mod in listedOrder)
            {
                if (mod.ModKey.FileName == outputModName) { continue; }

                var masterFiles = mod.Mod.ModHeader.MasterReferences.Select(x => x.Master.ToString());

                if (masterFiles.Contains(outputModName, StringComparer.OrdinalIgnoreCase)) 
                {
                    if (verbose) { Logger.CallTimedLogErrorWithStatusUpdateAsync(mod.ModKey.FileName.String + " will not be patched because it is mastered to a previous version of " + outputModName, ErrorType.Warning, 2); };
                    removedMods.Add(mod.ModKey.FileName.String);
                    continue;
                }

                bool isRemovedDependent = false;
                foreach (var removedMod in removedMods)
                {
                    if (masterFiles.Contains(removedMod, StringComparer.OrdinalIgnoreCase))
                    {
                        isRemovedDependent = true;
                        break;
                    }
                }
                if (isRemovedDependent)
                {
                    if (verbose) { Logger.CallTimedLogErrorWithStatusUpdateAsync(mod.ModKey.FileName + " will not be patched because it is mastered to a mod which is mastered to a previous version of " + outputModName, ErrorType.Warning, 2); }
                    continue;
                }

                filteredLoadOrder.Add(mod);
            }
            return filteredLoadOrder;
        }
    }
}
