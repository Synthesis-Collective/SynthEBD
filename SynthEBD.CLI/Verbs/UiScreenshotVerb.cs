using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Autofac;

namespace SynthEBD.CLI;

/// <summary>
/// Automated visual-QA harness: shows the real <c>MainWindow</c> against the real settings tree and
/// game environment, programmatically flips through every nav-panel menu, and captures a PNG of each
/// into <c>--out</c>. Menus are discovered by reflecting over <see cref="VM_NavPanel"/>'s public
/// <c>Click*</c> command properties, so menus added to the nav panel are picked up automatically.
///
/// <para>Deliberately never calls <c>MainWindow_ViewModel.Init()</c> — Init's only job is hooking the
/// application-Exit save of view models to disk, so skipping it guarantees a screenshot run can never
/// write the user's settings back.</para>
/// </summary>
public static class UiScreenshotVerb
{
    public static async Task<int> RunAsync(CliOptions options, TextWriter resultWriter)
    {
        // Program.cs constructed the Application as UiHarnessApp for this verb, whose XAML already
        // carries the GUI's resource set (loaded via InitializeComponent — the only mechanism under
        // which MahApps' deferred StaticResource references resolve; programmatic merging breaks them).
        if (Application.Current is not UiHarnessApp)
        {
            Console.Error.WriteLine("ui-screenshot: expected the UiHarnessApp application host.");
            return 2;
        }

        // Set BEFORE the bootstrap: settings load can migrate the disclosure mode, and no dialog
        // may ever block an automated run. MessageWindow modals (e.g. the FirstLaunch welcome,
        // update-handler migration notices) are swallowed and reported to stderr at the end of
        // the run instead of hanging the harness on an un-clickable dialog.
        UiModeController.Instance.SuppressModeChangeWarnings = true;
        MessageWindow.SuppressAllDialogs = true;

        if (!CliBootstrapper.TryCreate(options, out var bootstrapper, out string failureReason) || bootstrapper == null)
        {
            Console.Error.WriteLine("ui-screenshot: environment/settings failed to load: " + failureReason);
            return 2;
        }

        // A first-run flag on the loaded settings means the tree at the resolved root was empty —
        // the classic cause is pointing --synthebd-path at a portable settings folder (which has
        // no Settings\SettingsSource.json), making SynthEBDPaths look for Settings\Settings\*.
        // The captures would show a factory-fresh UI, so warn loudly.
        if (bootstrapper.PatcherState.GeneralSettings?.bFirstRun == true)
        {
            Console.Error.WriteLine("ui-screenshot: WARNING - the loaded settings look freshly defaulted (bFirstRun=true), so no existing "
                + "settings were found at the resolved root '" + bootstrapper.SettingsRootPath + "'. If this SynthEBD instance stores its "
                + "settings in a portable folder, pass that folder (the one containing 'Settings', 'Asset Packs', ...) via --settings-root "
                + "instead of --synthebd-path.");
        }

        using (bootstrapper)
        {
            // Resolving MainWindow_ViewModel transitively constructs ViewModelLoader, whose
            // construction populates every settings view model from the loaded models — the same
            // population the GUI performs at startup.
            var mainVM = bootstrapper.Container.Resolve<MainWindow_ViewModel>();
            var navPanel = bootstrapper.Container.Resolve<VM_NavPanel>();
            var displayedItem = bootstrapper.Container.Resolve<DisplayedItemVm>();

            var themes = options.Themes.Any() ? options.Themes : new List<string> { ThemeManager.DefaultThemeName };
            ThemeManager.ApplyTheme(themes[0]);

            var modes = options.Modes.Any() ? options.Modes : new List<UiDisplayMode> { UiModeController.Instance.DisplayMode };

            var window = new MainWindow
            {
                DataContext = mainVM,
                Left = 0,
                Top = 0,
            };
            // Override the constructor's screen-relative sizing for deterministic captures.
            window.Width = options.WindowWidth;
            window.Height = options.WindowHeight;
            window.Show();

            int captured = 0;
            foreach (var theme in themes)
            {
                // Theme hot-swap works on the live window (DynamicResource + implicit style
                // re-resolution) - the same mechanism the in-app theme picker uses.
                ThemeManager.ApplyTheme(theme);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;

                foreach (var mode in modes)
                {
                    UiModeController.Instance.DisplayMode = mode;
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;

                    int index = 0;
                    foreach (var (commandName, command) in EnumerateNavCommands(navPanel))
                    {
                        index++;
                        command.Execute(null);
                        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;

                        var menuName = displayedItem.DisplayedViewModel?.GetType().Name.RemovePrefix("VM_") ?? commandName;
                        if (options.Menus.Any()
                            && !options.Menus.Contains(menuName, StringComparer.OrdinalIgnoreCase)
                            && !options.Menus.Contains(commandName, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // Sub-navigation: execute any --invoke command whose menu matches the one on
                        // screen (e.g. an inner tab's Click* command on the menu's own VM), so the
                        // capture shows a tab the top-level nav flip can't reach.
                        foreach (var invoke in options.Invokes)
                        {
                            int dot = invoke.LastIndexOf('.');
                            string targetMenu = invoke.Substring(0, dot);
                            string commandProperty = invoke.Substring(dot + 1);
                            if (!targetMenu.Equals(menuName, StringComparison.OrdinalIgnoreCase)
                                && !targetMenu.Equals(commandName, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var displayedVm = displayedItem.DisplayedViewModel;
                            var commandProp = displayedVm?.GetType().GetProperty(commandProperty, BindingFlags.Public | BindingFlags.Instance);
                            if (displayedVm != null && commandProp != null
                                && typeof(ICommand).IsAssignableFrom(commandProp.PropertyType)
                                && commandProp.GetValue(displayedVm) is ICommand subCommand)
                            {
                                subCommand.Execute(null);
                                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
                            }
                            else
                            {
                                Console.Error.WriteLine("ui-screenshot: --invoke " + invoke + " did not resolve to an ICommand property on "
                                                        + (displayedVm?.GetType().Name ?? "<no displayed VM>") + "; skipped.");
                            }
                        }

                        if (options.ExpandExpanders)
                        {
                            // Iterate to a fixpoint: expanding an element materializes children (item
                            // containers, nested expanders, attribute cards) that only become visible
                            // to the next pass after a layout pump. Cap guards against a pathological
                            // expander that re-collapses itself.
                            for (int pass = 0; pass < 8 && ExpandAllExpanders(window) > 0; pass++)
                            {
                                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
                            }
                        }

                        if (options.ScrollTo != null)
                        {
                            if (ScrollToDocKey(window, options.ScrollTo))
                            {
                                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
                            }
                            else
                            {
                                Console.Error.WriteLine("ui-screenshot: --scroll-to " + options.ScrollTo
                                    + " matched no element with that DocTooltip.Key in menu " + menuName + "; capture is unscrolled.");
                            }
                        }

                        var fileName = index.ToString("D2") + "-" + menuName + ".png";
                        var filePath = Path.Combine(options.OutPath!, theme, mode.ToString(), fileName);
                        await WpfCapture.SaveWindowPngAsync(window, filePath, options.SettleMs);
                        Console.Error.WriteLine("ui-screenshot: captured " + theme + "\\" + mode + "\\" + fileName);
                        resultWriter.WriteLine(filePath);
                        captured++;
                    }
                }
            }

            window.Close();

            foreach (var suppressedDialog in MessageWindow.DrainSuppressedDialogs())
            {
                Console.Error.WriteLine("ui-screenshot: suppressed dialog - " + suppressedDialog);
            }

            Console.Error.WriteLine("ui-screenshot: " + captured + " screenshot(s) written to " + options.OutPath);
            return captured > 0 ? 0 : 1;
        }
    }

    /// <summary>
    /// The nav targets, as (command-name-without-Click-prefix, command) pairs in declaration order.
    /// </summary>
    private static IEnumerable<(string CommandName, ICommand Command)> EnumerateNavCommands(VM_NavPanel navPanel)
    {
        foreach (var property in typeof(VM_NavPanel)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType)
                                 && p.Name.StartsWith("Click", StringComparison.Ordinal))
                     .OrderBy(p => p.MetadataToken))
        {
            if (property.GetValue(navPanel) is ICommand command)
            {
                yield return (property.Name.Substring("Click".Length), command);
            }
        }
    }

    /// <summary>Scrolls the element carrying the given DocTooltip.Key to the top of its nearest
    /// ancestor ScrollViewer, so a below-the-fold control on a long settings page lands at the top
    /// of the capture. Returns false when no visible element carries the key (wrong menu, or the
    /// element sits inside a still-collapsed expander).</summary>
    private static bool ScrollToDocKey(DependencyObject root, string docKey)
    {
        var target = FindByDocKey(root, docKey);
        if (target == null)
        {
            return false;
        }

        var scrollViewer = FindAncestorScrollViewer(target);
        if (scrollViewer == null)
        {
            return false;
        }

        var offsetWithinViewport = target.TransformToAncestor(scrollViewer).Transform(new System.Windows.Point(0, 0));
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + offsetWithinViewport.Y);
        return true;
    }

    private static FrameworkElement? FindByDocKey(DependencyObject root, string docKey)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && string.Equals(DocTooltip.GetKey(fe), docKey, StringComparison.OrdinalIgnoreCase))
            {
                return fe;
            }
            if (FindByDocKey(child, docKey) is { } match)
            {
                return match;
            }
        }
        return null;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject element)
    {
        for (var current = VisualTreeHelper.GetParent(element); current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }
        }
        return null;
    }

    /// <summary>Expands every collapsed Expander currently in the window's visual tree, plus the
    /// disclosure toggles that stand in for Expanders in a few migrated controls (ToggleButtons named
    /// CardExpandToggle - the attribute rule editor's cards - and DescriptorEditToggle - the descriptor
    /// selector's Category/Value editor, which is gated by a named toggle rather than a real Expander).
    /// Returns how many elements this pass expanded; the caller pumps layout and repeats until no
    /// newly-materialized collapsed elements remain.</summary>
    private static int ExpandAllExpanders(DependencyObject root)
    {
        int expanded = 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Expander { IsExpanded: false } expander)
            {
                expander.IsExpanded = true;
                expanded++;
            }
            else if (child is System.Windows.Controls.Primitives.ToggleButton { IsChecked: not true } toggle
                     && toggle.Name is "CardExpandToggle" or "DescriptorEditToggle")
            {
                toggle.IsChecked = true;
                expanded++;
            }
            expanded += ExpandAllExpanders(child);
        }
        return expanded;
    }

    private static string RemovePrefix(this string value, string prefix)
    {
        return value.StartsWith(prefix, StringComparison.Ordinal) ? value.Substring(prefix.Length) : value;
    }
}
