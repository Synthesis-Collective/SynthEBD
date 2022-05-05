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
                return new PatcherEnvironment(PatcherSettings.SkyrimVersion);
            });

        public static PatcherEnvironment Environment => _env.Value;

        public static IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> TryAllEnvironments()
        {
            //SkyrimRelease[] releases = new SkyrimRelease[] { SkyrimRelease.SkyrimSE, SkyrimRelease.SkyrimVR, SkyrimRelease.SkyrimLE, SkyrimRelease.EnderalSE, SkyrimRelease.EnderalLE };
            SkyrimRelease[] releases = new SkyrimRelease[] { SkyrimRelease.EnderalLE };

            foreach (var release in releases)
            {
                try
                {
                    return GameEnvironment.Typical.Skyrim(release, LinkCachePreferences.OnlyIdentifiers());
                }
                catch { 
                    continue; 
                }
            }

            var customEnvWindow = new Window_CustomEnvironment();
            var customEnvVM = new VM_CustomEnvironment(customEnvWindow);
            customEnvWindow.DataContext = customEnvVM;
            customEnvWindow.ShowDialog();

            if (customEnvVM.IsValidated)
            {
                PatcherSettings.SkyrimVersion = customEnvVM.SkyrimRelease;
                PatcherSettings.CustomGamePath = customEnvVM.CustomGamePath;
                return customEnvVM.Environment;
            }
            else
            {
                System.Windows.Application.Current.Shutdown();
                System.Environment.Exit(1);
            }
           
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

        private static IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> OriginState { get; set; }

        private static GameRelease SkyrimReleaseToGameRelease(SkyrimRelease gameType)
        {
            switch(gameType)
            {
                case SkyrimRelease.SkyrimSE: return GameRelease.SkyrimSE;
                case SkyrimRelease.SkyrimLE: return GameRelease.SkyrimLE;
                case SkyrimRelease.SkyrimVR: return GameRelease.SkyrimVR;
                case SkyrimRelease.EnderalSE: return GameRelease.EnderalSE;
                case SkyrimRelease.EnderalLE: return GameRelease.EnderalLE;
                default: return new GameRelease();
            }
        }

        public static IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> BuildCustomEnvironment(string executablePath, SkyrimRelease skyrimRelease) // this function expects calling function to handle exceptions
        {
            var gameDir = System.IO.Path.GetDirectoryName(executablePath);
            var dataDir = System.IO.Path.Combine(gameDir, "data");
            return  GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(SkyrimReleaseToGameRelease(skyrimRelease)).WithTargetDataFolder(dataDir).Build();
        }

        public void GetOriginState(SkyrimRelease? gameType)
        {
            if (gameType is not null)
            {
                if (!string.IsNullOrWhiteSpace(PatcherSettings.CustomGamePath))
                {
                    try
                    {
                        OriginState = BuildCustomEnvironment(PatcherSettings.CustomGamePath, gameType.Value);
                        Logger.TimedNotifyStatusUpdate("Built environment from " + PatcherSettings.CustomGamePath, ErrorType.Warning, 3);
                    }
                    catch
                    {
                        Logger.TimedNotifyStatusUpdate("Could not build environment from " + PatcherSettings.CustomGamePath, ErrorType.Error, 5);
                    }
                }
                else
                {
                    GetDefaultOriginState(gameType);
                }
            }
            else
            {
                OriginState = PatcherEnvironmentProvider.TryAllEnvironments();
            }
        }

        public void GetDefaultOriginState(SkyrimRelease? gameType)
        {
            try
            {
                OriginState = GameEnvironment.Typical.Skyrim(gameType.Value, LinkCachePreferences.OnlyIdentifiers());
            }
            catch
            {
                OriginState = PatcherEnvironmentProvider.TryAllEnvironments();
            }
        }

        public PatcherEnvironment(SkyrimRelease? gameType)
        {
            GetOriginState(gameType);

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
            GameRelease = OriginState.GameRelease;
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
            GetOriginState(gameType);

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
            OriginState = GameEnvironment.Typical.Skyrim(PatcherSettings.SkyrimVersion, LinkCachePreferences.OnlyIdentifiers());
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
