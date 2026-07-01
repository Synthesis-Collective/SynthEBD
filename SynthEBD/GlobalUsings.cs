// ReactiveUI 20+ modularization shim.
//
// Bumping Mutagen.Bethesda.WPF to 0.54.0 pulls ReactiveUI 23.x (via Noggog.WPF), which removed
// the static `RxApp` class and moved its schedulers to `ReactiveUI.RxSchedulers`. SynthEBD only
// ever uses RxApp.MainThreadScheduler and RxApp.TaskpoolScheduler (verified: all 35 call sites),
// both of which exist verbatim on RxSchedulers, so aliasing RxApp -> RxSchedulers keeps every
// existing `RxApp.MainThreadScheduler` / `RxApp.TaskpoolScheduler` call site compiling unchanged.
//
// (The `DisposeWith` extension also moved out of System.Reactive in this bump, but SynthEBD's
//  `.DisposeWith(this)` idiom binds to Noggog.CSharpExt's own IDisposableDropoff overload — already
//  in scope via `using Noggog;` — so those call sites continue to resolve without a shim.)
global using RxApp = ReactiveUI.RxSchedulers;
