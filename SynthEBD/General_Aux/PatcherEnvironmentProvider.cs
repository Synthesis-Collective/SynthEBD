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
                    return TryStartEnvironment();
                }
                
            });

        public static PatcherEnvironment Environment => _env.Value;

        public static PatcherEnvironment TryStartEnvironment()
        {
            try
            {
                return new PatcherEnvironment(SkyrimRelease.SkyrimSE);
            }
            catch { };
            try
            {
                new PatcherEnvironment(SkyrimRelease.SkyrimVR);
            }
            catch { };
            try
            {
                new PatcherEnvironment(SkyrimRelease.SkyrimLE);
            }
            catch { };
            try
            {
                new PatcherEnvironment(SkyrimRelease.EnderalSE);
            }
            catch { };
            try
            {
                new PatcherEnvironment(SkyrimRelease.EnderalLE);
            }
            catch { };

            MessageBox.Show("Environment creation failed. Could not detect any supported versions of Skyirm.");
            System.Windows.Application.Current.Shutdown();
            return null;
        }
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

        public static IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> GetOriginState(SkyrimRelease gameType)
        {
            try
            {
                return GameEnvironment.Typical.Skyrim(gameType, LinkCachePreferences.OnlyIdentifiers());
            }
            catch
            {
                return PatcherEnvironmentProvider.TryStartEnvironment();
            }
        }

        public PatcherEnvironment(SkyrimRelease gameType)
        {
            using (var originState = GetOriginState(gameType))
            {
                if (PatcherSettings.General != null)
                {
                    LoadOrder = (ILoadOrder<IModListing<ISkyrimModGetter>>)originState.LoadOrder.ListedOrder
                        .OnlyEnabledAndExisting()
                        .RemoveModAndDependents(PatcherSettings.General.PatchFileName + ".esp", false) // remove the output plugin and any plugins mastered to previous versions of it
                        .ToLoadOrder();
                }
                else
                {
                    LoadOrder = originState.LoadOrder;
                }
                LinkCache = LoadOrder.ToMutableLinkCache();

                DataFolderPath = originState.DataFolderPath;
                LoadOrderFilePath = originState.LoadOrderFilePath;
                CreationClubListingsFilePath = originState.CreationClubListingsFilePath;
            }
        }

        public void Refresh(string outputModName, bool verbose)
        {
            SkyrimRelease game;
            if (PatcherSettings.General != null)
            {
                game = PatcherSettings.General.SkyrimVersion;
            }
            else
            {
                game = SkyrimRelease.SkyrimSE;
            }

            using (var originState = GetOriginState(game))
            {
                if (PatcherSettings.General != null)
                {
                    LoadOrder = (ILoadOrder<IModListing<ISkyrimModGetter>>)originState.LoadOrder.ListedOrder
                        .OnlyEnabledAndExisting()
                        .RemoveModAndDependents(outputModName + ".esp", verbose) // remove the output plugin and any plugins mastered to previous versions of it
                        .ToLoadOrder();
                }
                else
                {
                    LoadOrder = originState.LoadOrder;
                }
                LinkCache = LoadOrder.ToMutableLinkCache();

                DataFolderPath = originState.DataFolderPath;
                LoadOrderFilePath = originState.LoadOrderFilePath;
                CreationClubListingsFilePath = originState.CreationClubListingsFilePath;
            }
        }

        public void Refresh(bool verbose)
        {
            SkyrimRelease game;
            if (PatcherSettings.General != null)
            {
                game = PatcherSettings.General.SkyrimVersion;
            }
            else
            {
                game = SkyrimRelease.SkyrimSE;
            }
            using (var originState = GetOriginState(game))
            {
                if (PatcherSettings.General != null)
                {
                    LoadOrder = (ILoadOrder<IModListing<ISkyrimModGetter>>)originState.LoadOrder.ListedOrder
                        .OnlyEnabledAndExisting()
                        .RemoveModAndDependents(PatcherSettings.General.PatchFileName + ".esp", verbose) // remove the output plugin and any plugins mastered to previous versions of it
                        .ToLoadOrder();
                }
                else
                {
                    LoadOrder = originState.LoadOrder;
                }
                LinkCache = LoadOrder.ToMutableLinkCache();

                DataFolderPath = originState.DataFolderPath;
                LoadOrderFilePath = originState.LoadOrderFilePath;
                CreationClubListingsFilePath = originState.CreationClubListingsFilePath;
            }
        }

        public void RefreshAndChangeGameType(SkyrimRelease gameType, string outputModName)
        {
            using (var originState = GetOriginState(gameType))
            {
                if (PatcherSettings.General != null)
                {
                    LoadOrder = (ILoadOrder<IModListing<ISkyrimModGetter>>)originState.LoadOrder.ListedOrder
                        .OnlyEnabledAndExisting()
                        .RemoveModAndDependents(outputModName + ".esp", false) // remove the output plugin and any plugins mastered to previous versions of it
                        .ToLoadOrder();
                }
                else
                {
                    LoadOrder = originState.LoadOrder;
                }
                LinkCache = LoadOrder.ToMutableLinkCache();

                DataFolderPath = originState.DataFolderPath;
                LoadOrderFilePath = originState.LoadOrderFilePath;
                CreationClubListingsFilePath = originState.CreationClubListingsFilePath;
            };
        }

        public void SuspendEnvironment()
        {

        }
        public void ResumeEnvironment()
        { 
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
