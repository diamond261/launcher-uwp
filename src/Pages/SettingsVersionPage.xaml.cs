using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Flarial.Launcher.Pages;

public partial class SettingsVersionPage : Page
{
    readonly Settings _settings = Settings.Current;

    readonly List<string> _items = [];

    static IEnumerable<string> ParsePaths(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value
            .Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim().Trim('"'))
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    void Persist()
    {
        _settings.CustomDllPath = string.Join(";", _items);
        DllListBox.ItemsSource = null;
        DllListBox.ItemsSource = _items;
    }

    public SettingsVersionPage()
    {
        InitializeComponent();

        _items.AddRange(ParsePaths(_settings.CustomDllPath));
        Persist();
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            InitialDirectory = @"C:\",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = true,
            DefaultExt = "dll",
            Filter = "DLL Files|*.dll;",
            Title = "Select Custom DLL(s)"
        };

        if (dialog.ShowDialog() is not true)
            return;

        foreach (var file in dialog.FileNames)
            if (!_items.Contains(file, StringComparer.OrdinalIgnoreCase))
                _items.Add(file);

        Persist();
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (DllListBox.SelectedItem is not string selected)
            return;

        _items.RemoveAll(value => value.Equals(selected, StringComparison.OrdinalIgnoreCase));
        Persist();
    }

    void Up_Click(object sender, RoutedEventArgs e)
    {
        if (DllListBox.SelectedItem is not string selected)
            return;

        var index = _items.FindIndex(value => value.Equals(selected, StringComparison.OrdinalIgnoreCase));
        if (index <= 0)
            return;

        (_items[index - 1], _items[index]) = (_items[index], _items[index - 1]);
        Persist();
        DllListBox.SelectedItem = selected;
    }

    void Down_Click(object sender, RoutedEventArgs e)
    {
        if (DllListBox.SelectedItem is not string selected)
            return;

        var index = _items.FindIndex(value => value.Equals(selected, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index >= _items.Count - 1)
            return;

        (_items[index], _items[index + 1]) = (_items[index + 1], _items[index]);
        Persist();
        DllListBox.SelectedItem = selected;
    }
}
