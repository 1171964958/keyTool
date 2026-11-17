using System;
using System.Drawing;
using System.Windows;
using KeyTool.Windows.ViewModels;
using Forms = System.Windows.Forms;

namespace KeyTool.Windows;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _viewModel = new MainViewModel();
        _mainWindow = new MainWindow
        {
            DataContext = _viewModel
        };
        MainWindow = _mainWindow;

        var processPath = Environment.ProcessPath;
        var icon = processPath is null
            ? SystemIcons.Application
            : Icon.ExtractAssociatedIcon(processPath) ?? SystemIcons.Application;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示 KeyTool", null, (_, _) => ShowMainWindow());
        menu.Items.Add("停止所有任务", null, (_, _) => _viewModel.StopAllTasks());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 KeyTool", null, (_, _) => ExitApplication());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "KeyTool",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        _mainWindow.Show();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }
            _mainWindow.Activate();
        });
    }

    private void ExitApplication()
    {
        Dispatcher.Invoke(() =>
        {
            _viewModel?.Shutdown();
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            _mainWindow?.ForceClose();
            Shutdown();
        });
    }
}
