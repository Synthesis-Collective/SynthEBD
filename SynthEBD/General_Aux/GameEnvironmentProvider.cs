using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class GameEnvironmentProvider
    {
        public static readonly GameEnvironmentProvider Instance = new();

        private static Lazy<IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter>> _env = new(
            () =>
            {
                var gameRelease = SkyrimRelease.SkyrimSE;
                return GameEnvironment.Typical.Skyrim(gameRelease, LinkCachePreferences.OnlyIdentifiers());
            });

        public static IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> MyEnvironment => _env.Value;
        /*
        public class PatcherEnvironment : IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter>
        {
            private bool disposedValue;

            public ILoadOrder<IModListing<ISkyrimModGetter>> LoadOrder { get; }

            ILoadOrder<IModListing<ISkyrimModGetter>> IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter>.LoadOrder{ get; }

            ILinkCache<ISkyrimMod, ISkyrimModGetter> IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter>.LinkCache{ get; }

            ILoadOrder<IModListing<ISkyrimModGetter>> IGameEnvironmentState<ISkyrimModGetter>.LoadOrder{ get; }

            DirectoryPath IGameEnvironmentState.DataFolderPath{ get; }

            GameRelease IGameEnvironmentState.GameRelease{ get; }

            FilePath IGameEnvironmentState.LoadOrderFilePath{ get; }

            FilePath? IGameEnvironmentState.CreationClubListingsFilePath{ get; }

            ILoadOrderGetter<IModListingGetter<IModGetter>> IGameEnvironmentState.LoadOrder{ get; }

            ILinkCache IGameEnvironmentState.LinkCache{ get; }


            public PatcherEnvironment(IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> source, ISkyrimMod outputMod)
            {
                var test = GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE, LinkCachePreferences.OnlyIdentifiers());
                var x = test.LoadOrder.ListedOrder;
                var y = test.LoadOrder;

                LoadOrder = source.LoadOrder.ListedOrder
                    .OnlyEnabledAndExisting()
                    .ToLoadOrder();
                LinkCache = LoadOrder.ToMutableLinkCache();
            }

            private static ILoadOrder<IModListing<ISkyrimModGetter>> RemoveModAndDependents(IEnumerable<IModListing<ISkyrimModGetter>> listedOrder, ISkyrimMod outputMod)
            {
                IEnumerable<IModListing<ISkyrimModGetter>> filteredLoadOrder = new List<IModListing<ISkyrimModGetter>>();
                HashSet<string> removedMods = new HashSet<string>();
                foreach (var mod in listedOrder)
                {
                    if (mod.ModKey.FileName == outputMod.ModKey.FileName) { continue; }
                    if (mod.Mod.ContainedFormLinks.Where(x => x.FormKey.ModKey.FileName == outputMod.ModKey.FileName).Any())
                    {
                        removedMods.Add(mod.ModKey.FileName);
                        continue;
                    }
                    bool isRemovedDependent = false;
                    foreach (var removedMod in removedMods)
                    {
                        if (mod.Mod.ContainedFormLinks.Where(x => x.FormKey.ModKey.FileName == removedMod).Any())
                        {
                            isRemovedDependent = true;
                            break;
                        }
                    }
                    if (isRemovedDependent)
                    {
                        continue;
                    }

                    filteredLoadOrder.
                }
            }

            protected virtual void Dispose(bool disposing)
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
        }*/
    }
}
