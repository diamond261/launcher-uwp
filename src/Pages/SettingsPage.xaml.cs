using System;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Flarial.Launcher.Animations;
using Flarial.Launcher.Functions;
using Flarial.Launcher.Styles;

namespace Flarial.Launcher.Pages;

/// <summary>
/// Interaction logic for SettingsPage.xaml
/// </summary>
public partial class SettingsPage : Page
{
    static SettingsPage s_instance;

    public SettingsPage()
    {
        InitializeComponent();
        s_instance = this;

        GeneralPageButton.IsChecked = true;
        SetDllPageVisibility(Settings.Current.DllBuild is DllBuild.Custom);
    }

    public static Border b1;
    public static Grid MainGrid;

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        => SettingsPageTransition.SettingsLeaveAnimation(b1, MainGrid);

    private void Navigate_General(object sender, RoutedEventArgs e)
    {
        SettingsPageTransition.SettingsNavigateAnimation(0, PageBorder, PageStackPanel);
    }

    private void Navigate_Dll(object sender, RoutedEventArgs e)
    {
        SettingsPageTransition.SettingsNavigateAnimation(-500, PageBorder, PageStackPanel);
    }

    private void Navigate_Backups(object sender, RoutedEventArgs e)
    {
        SettingsPageTransition.SettingsNavigateAnimation(-1000, PageBorder, PageStackPanel);
    }

    internal static void SetDllPageVisibility(bool visible)
    {
        if (s_instance is null)
            return;

        if (visible)
        {
            s_instance.DllPageButton.Visibility = Visibility.Visible;
            s_instance.DllPageButton.Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            s_instance.DllPageButton.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (_, _) =>
            {
                s_instance.DllPageButton.Visibility = Visibility.Collapsed;
            };
            s_instance.DllPageButton.BeginAnimation(OpacityProperty, fadeOut);
        }

        if (!visible && s_instance.DllPageButton.IsChecked is true)
            s_instance.GeneralPageButton.IsChecked = true;
    }
}
