using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Diagnostics;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private NativeGodotLauncher? _androidLauncherFallback;

    private void LoadNativeLiveStackAndroid()
    {
        var source = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was not configured.");

        FosNvLog.Info(
            "native_android_dispatch",
            $"campaign={source.Campaign} game={source.Game} pluginSources={source.PluginSources.Count}");
        SetLoadingStatus("INDEXING LIVE PLUGINS");
        ShowNativeLiveMenuAndroid(source.PluginSources);
        DismissLoadingScreen();
    }

    private void ShowNativeLiveMenuAndroid(IReadOnlyList<FalloutPluginSource> sources)
    {
        FosNvLog.Info("native_menu_create", $"sources={sources.Count}");

        var layer = new CanvasLayer { Name = "NativeLiveMenu", Layer = NativeMenuCanvasLayer };
        var menu = new NativeGamebryoStartMenu(action =>
        {
            FosNvLog.Info("native_menu_action", $"action={action}");

            if (action == "sQuit")
            {
                FosNvLog.Info("native_menu_quit", "userRequested=true");
                GetTree().Quit();
            }
            else if (action == "sLoad")
            {
                OpenNativeSessionMenu(showSaves: true, layer.GetChild<Control>(0));
            }
            else if (action is "sNew" or "sContinue")
            {
                if (_nativeStartingGame)
                    return;

                _nativeStartingGame = true;
                _nativeContinueOpening = action != "sNew";
                StartNativeGameFromAndroidMenu(layer, action);
            }
            else
            {
                SetMeta("opennv_ui_divergence", $"StartMenu action has no retail-equivalent owner: {action}");
                FosNvLog.Error("native_menu_action_unowned", $"action={action}");
                GD.PushError($"OPENNV_UI_DIVERGENCE menu=StartMenu action={action} owner=missing");
            }
        });

        layer.AddChild(menu);
        AddChild(layer);
        IndexNativeLiveStackForAndroidMenu(sources, menu, layer);
    }

    private async void IndexNativeLiveStackForAndroidMenu(
        IReadOnlyList<FalloutPluginSource> sources,
        NativeGamebryoStartMenu menu,
        CanvasLayer layer)
    {
        try
        {
            var source = RuntimeLiveContentSource.Current ??
                throw new InvalidOperationException("Live retail source disappeared before indexing.");
            var executable = Path.Combine(Path.GetDirectoryName(source.ContentRoot)!, "FalloutNV.exe");

            FosNvLog.Info(
                "native_stack_index_begin",
                $"campaign={source.Campaign} sources={sources.Count} contentRoot={source.ContentRoot} " +
                $"falloutNvExe={executable} falloutNvExeExists={File.Exists(executable)}");

            await Task.Run(() => IndexNativeLiveStack(sources));

            var initialCell = _nativeInitialCell ??
                throw new InvalidOperationException("Native initial CELL was not decoded.");
            var stack = _nativePluginStack ??
                throw new InvalidOperationException("Native plugin stack was not indexed.");

            FosNvLog.Info(
                "native_stack_index_complete",
                $"plugins={stack.Plugins.Count} initialCell={initialCell.Cell.FormKey}");

            var transition = RuntimeLiveContentSource.Current?.Campaign ==
                RuntimeLiveContentSource.FalloutNewVegasGame
                ? FalloutDoorTransitionResolver.ResolveInteriorExits(stack, initialCell).Single()
                : null;

            if (_nativeOpeningRestore is null)
            {
                FosNvLog.Info("native_cell_prewarm_begin", $"cell={initialCell.Cell.FormKey}");
                _nativePrewarmedInitialCellRoot =
                    BuildNativeCellRoot(initialCell, transition, sourceSide: true);
                FosNvLog.Info(
                    "native_cell_prewarm_complete",
                    $"cell={initialCell.Cell.FormKey} children={_nativePrewarmedInitialCellRoot.GetChildCount()}");
            }

            if (_nativeOpeningControls is not null)
            {
                FosNvLog.Info("native_quest_scripts_begin", "openingControls=true");
                CreateNativeQuestScripts();
                FosNvLog.Info("native_quest_scripts_complete", "openingControls=true");
            }

            menu.SetReady(stack, _nativeOpeningRestore is not null);
            FosNvLog.Info(
                "native_menu_ready",
                $"plugins={stack.Plugins.Count} canContinue={_nativeOpeningRestore is not null}");

            ReleaseAndroidLauncherFallback();

            if (_continueAfterRestart && _nativeOpeningRestore is not null)
            {
                _continueAfterRestart = false;
                _nativeStartingGame = true;
                _nativeContinueOpening = true;
                StartNativeGameFromAndroidMenu(menu.GetParent<CanvasLayer>(), "sLoad");
            }
        }
        catch (Exception exception)
        {
            FosNvLog.Exception(
                "native_stack_fail",
                exception,
                $"campaign={RuntimeLiveContentSource.Current?.Campaign ?? "<none>"}");
            GD.PushError($"OPENNV_NATIVE_STACK_FAIL {exception}");
            _nativeStartingGame = false;

            if (GodotObject.IsInstanceValid(layer))
                layer.QueueFree();

            RestoreAndroidLauncherAfterNativeFailure(exception.Message);
        }
    }

    private async void StartNativeGameFromAndroidMenu(CanvasLayer layer, string action)
    {
        var menu = layer.GetChild<NativeGamebryoStartMenu>(0);
        menu.ShowLoading(_nativeContinueOpening);
        FosNvLog.Info(
            "native_game_load_begin",
            $"action={action} continue={_nativeContinueOpening}");
        GD.Print($"OPENNV_NATIVE_MENU_LOAD action={action} save={_nativeContinueOpening}");

        try
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            FosNvLog.Info("native_game_load_cell_begin", $"action={action}");
            LoadNativeInitialCell();
            FosNvLog.Info(
                "native_game_ready",
                $"action={action} cell={_nativeActiveCell?.Cell.FormKey.ToString() ?? "<unknown>"}");
            layer.QueueFree();
        }
        catch (Exception exception)
        {
            _nativeStartingGame = false;
            FosNvLog.Exception(
                "native_game_load_fail",
                exception,
                $"action={action} continue={_nativeContinueOpening}");
            GD.PushError($"OPENNV_NATIVE_MENU_LOAD_FAIL {exception}");
            menu.ShowLoadFailure();
        }
    }

    private void ReleaseAndroidLauncherFallback()
    {
        var launcher = _androidLauncherFallback;
        _androidLauncherFallback = null;
        if (launcher is null || !GodotObject.IsInstanceValid(launcher))
            return;

        launcher.QueueFree();
        FosNvLog.Info("android_launcher_release", "nativeMenuReady=true");
    }

    private void RestoreAndroidLauncherAfterNativeFailure(string reason)
    {
        var launcher = _androidLauncherFallback;
        _androidLauncherFallback = null;

        if (launcher is null || !GodotObject.IsInstanceValid(launcher))
        {
            FosNvLog.Error(
                "android_launcher_restore_fail",
                $"reason={reason} launcherAvailable=false");
            return;
        }

        RuntimeLiveContentSource.Clear();
        launcher.Show();
        launcher.ReportLaunchFailure(
            "New Vegas startup failed while loading its native data. " +
            "The app was kept open so the failure can be diagnosed. " +
            "Check Download/FOS NV/fosnv.log.\n\n" + reason);
        FosNvLog.Info("android_launcher_restored", $"reason={reason}");
    }
}
