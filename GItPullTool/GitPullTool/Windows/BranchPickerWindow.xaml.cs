using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace GitPullTool.Windows;

public partial class BranchPickerWindow : Window
{
    private readonly ObservableCollection<string> branches;
    private readonly ICollectionView view;

    public BranchPickerWindow(IEnumerable<string> branchList, string? repoName)
        : this(branchList, repoName, "切换分支", "请选择分支：")
    {
    }

    public BranchPickerWindow(IEnumerable<string> branchList, string? repoName, string title, string prompt)
    {
        InitializeComponent();

        Title = string.IsNullOrWhiteSpace(repoName) ? title : $"{title} - {repoName}";
        PromptTextBlock.Text = prompt;

        branches = new ObservableCollection<string>(branchList);
        view = CollectionViewSource.GetDefaultView(branches);
        view.Filter = FilterBranch;

        BranchList.ItemsSource = view;
        BranchList.MouseDoubleClick += (_, _) => ConfirmSelection();
        BranchList.KeyDown += OnListKeyDown;
        FilterBox.TextChanged += (_, _) => view.Refresh();

        if (branches.Count > 0)
        {
            BranchList.SelectedIndex = 0;
        }
    }

    public string? SelectedBranch => BranchList.SelectedItem as string;

    private bool FilterBranch(object obj)
    {
        if (obj is not string name)
        {
            return false;
        }

        var keyword = FilterBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void OnListKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            ConfirmSelection();
        }
    }

    private void ConfirmSelection()
    {
        if (SelectedBranch is null)
        {
            return;
        }

        DialogResult = true;
        Close();
    }
}
