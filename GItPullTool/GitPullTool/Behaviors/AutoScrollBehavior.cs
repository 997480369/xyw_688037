using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using WpfListBox = System.Windows.Controls.ListBox;

namespace GitPullTool.Behaviors;

public static class AutoScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(AutoScrollBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty CollectionChangedHandlerProperty = DependencyProperty.RegisterAttached(
        "CollectionChangedHandler",
        typeof(NotifyCollectionChangedEventHandler),
        typeof(AutoScrollBehavior),
        new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WpfListBox listBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            listBox.Loaded += OnLoaded;
            listBox.Unloaded += OnUnloaded;
            Subscribe(listBox);
        }
        else
        {
            listBox.Loaded -= OnLoaded;
            listBox.Unloaded -= OnUnloaded;
            Unsubscribe(listBox);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfListBox listBox)
        {
            Subscribe(listBox);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfListBox listBox)
        {
            Unsubscribe(listBox);
        }
    }

    private static void Subscribe(WpfListBox listBox)
    {
        Unsubscribe(listBox);

        if (listBox.Items is not INotifyCollectionChanged collection)
        {
            return;
        }

        NotifyCollectionChangedEventHandler handler = (_, _) => QueueScrollToEnd(listBox);
        collection.CollectionChanged += handler;
        listBox.SetValue(CollectionChangedHandlerProperty, handler);
    }

    private static void Unsubscribe(WpfListBox listBox)
    {
        if (listBox.Items is INotifyCollectionChanged collection
            && listBox.GetValue(CollectionChangedHandlerProperty) is NotifyCollectionChangedEventHandler handler)
        {
            collection.CollectionChanged -= handler;
        }

        listBox.ClearValue(CollectionChangedHandlerProperty);
    }

    private static void QueueScrollToEnd(WpfListBox listBox)
    {
        if (!listBox.IsLoaded)
        {
            return;
        }

        _ = listBox.Dispatcher.BeginInvoke(
            () => ScrollToEnd(listBox),
            DispatcherPriority.ContextIdle);
    }

    private static void ScrollToEnd(WpfListBox listBox)
    {
        if (!GetIsEnabled(listBox) || !listBox.IsLoaded || listBox.Items.Count == 0)
        {
            return;
        }

        var lastItem = listBox.Items[listBox.Items.Count - 1];
        try
        {
            listBox.ScrollIntoView(lastItem);
        }
        catch (InvalidOperationException)
        {
            // The owning ItemsControl can be temporarily inconsistent during tab switches.
        }
    }
}
