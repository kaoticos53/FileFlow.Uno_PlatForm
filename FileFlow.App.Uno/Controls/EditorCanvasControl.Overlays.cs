using System;
using System.Collections.Generic;
using FileFlow.App.Services;
using FileFlow.App.Uno.Platform;
using FileFlow.App.ViewModels;
using FileFlow.Sdk;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FileFlow.App.Uno.Controls;

/// <summary>
/// Capas superpuestas del lienzo de edición (decoradores, spotlight, avisos y migas de pan).
/// </summary>
public sealed partial class EditorCanvasControl
{
    private EditorViewModel? _notifiedEditor;

    /// <summary>Refresca el banner con el estado del VM del núcleo (texto + filas de arreglo).</summary>
    private void RefreshCanvasNotice()
    {
        if (_editor is null)
        {
            return;
        }

        _notifiedEditor = _editor;
        bool hasNotice = _editor.HasCanvasNotice || _editor.HasCanvasNoticeFixes;

        CanvasNoticeBanner.Visibility = hasNotice ? Visibility.Visible : Visibility.Collapsed;
        CanvasNoticeText.Text = _editor.CanvasNotice;
        CanvasNoticeFixes.ItemsSource = _editor.HasCanvasNoticeFixes ? _editor.CanvasNoticeFixes : null;
    }

    private void OnDismissCanvasNotice(object sender, RoutedEventArgs e)
    {
        _notifiedEditor?.DismissCanvasNoticeCommand.Execute(null);
        RefreshCanvasNotice();
    }

    private void OnGoToFixNode(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DroppedConnectionFixViewModel fix)
        {
            fix.GoToNodeCommand.Execute(null);
        }
    }

    private void OnReconnectFix(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DroppedConnectionFixViewModel fix && fix.CanReconnect)
        {
            fix.ReconnectCommand.Execute(null);
            UpdatePortStatesAndWires();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Fase 3.4: decoradores (notas/grupos), drag & drop, spotlight y migas de subflujos
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Reconstruye la capa de decoradores: grupos AL FONDO (z menor), notas delante.</summary>
    private void RebuildDecorators()
    {
        DecoratorLayer.Children.Clear();
        _decoratorBindings.Clear();
        if (_editor is null)
        {
            return;
        }

        // El mismo orden del CanvasDecorators del núcleo: grupos insertados en 0 (detrás), notas al final.
        foreach (var decorator in _editor.CanvasDecorators)
        {
            ContentPresenter container;
            switch (decorator)
            {
                case GroupViewModel group:
                    container = WrapDecorator(BuildGroupCard(group), group);
                    DecoratorLayer.Children.Insert(0, container);
                    break;
                case AnnotationViewModel annotation:
                    container = WrapDecorator(BuildAnnotationCard(annotation), annotation);
                    DecoratorLayer.Children.Add(container);
                    break;
            }
        }
    }

    private readonly Dictionary<object, FrameworkElement> _decoratorBindings = new();

    private ContentPresenter WrapDecorator(FrameworkElement card, object decorator)
    {
        var container = new ContentPresenter { Content = card };
        PositionDecorator(container, decorator);
        _decoratorBindings[decorator] = card;
        return container;
    }

    private void PositionDecorator(ContentPresenter container, object decorator)
    {
        // La posición sale proyectada por el mismo conversor del 217 (jamás de .X/.Y crudos).
        var projected = UnoPointConverter.Instance.Convert(DecoratorLocation(decorator), typeof(Windows.Foundation.Point), null!, "en-US");
        if (projected is Windows.Foundation.Point p)
        {
            Canvas.SetLeft(container, p.X);
            Canvas.SetTop(container, p.Y);
        }
    }

    private static Sdk.Point DecoratorLocation(object decorator) => decorator switch
    {
        GroupViewModel g => g.Location,
        AnnotationViewModel a => a.Location,
        _ => new Sdk.Point(0, 0)
    };

    /// <summary>La nota: cara de color, título y contenido; botones de recolorear y borrar.</summary>
    private FrameworkElement BuildAnnotationCard(AnnotationViewModel annotation)
    {
        var colorBrush = new SolidColorBrush(NodeCardViewModel.ParseHex(annotation.Color));
        var title = new TextBlock
        {
            Text = annotation.Title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black)
        };
        var content = new TextBlock
        {
            Text = annotation.Content,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black)
        };
        var delete = new Button { Content = "✕", Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(4, 0, 4, 0) };
        delete.Click += (_, _) =>
        {
            _editor?.DeleteAnnotationCommand.Execute(annotation);
            RebuildDecorators();
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(title);
        header.Children.Add(delete);

        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(header);
        body.Children.Add(content);

        var card = new Border
        {
            Background = colorBrush,
            BorderBrush = new SolidColorBrush(NodeCardViewModel.ParseHex("#30363D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Width = annotation.Width,
            Child = body,
            Tag = annotation
        };
        HookDecoratorDrag(card, annotation);
        return card;
    }

    /// <summary>El grupo: contorno de color translúcido con el título; engloba a sus nodos.</summary>
    private FrameworkElement BuildGroupCard(GroupViewModel group)
    {
        var accent = NodeCardViewModel.ParseHex(group.Color);
        var title = new TextBlock
        {
            Text = group.Title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            Foreground = new SolidColorBrush(NodeCardViewModel.ParseHex("#F0F6FC"))
        };
        var delete = new Button { Content = "✕", Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(4, 0, 4, 0), Foreground = new SolidColorBrush(NodeCardViewModel.ParseHex("#F0F6FC")) };
        delete.Click += (_, _) =>
        {
            _editor?.DeleteGroupCommand.Execute(group);
            RebuildDecorators();
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        header.Children.Add(title);
        header.Children.Add(delete);

        var card = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Width = group.Width,
            Height = group.Height,
            Child = header,
            Tag = group,
            IsHitTestVisible = true
        };
        HookDecoratorDrag(card, group);
        return card;
    }

    /// <summary>Arrastre de decoradores: el puntero mueve la Location del VM (Sdk.Point) y repinta.</summary>
    private void HookDecoratorDrag(FrameworkElement card, object decorator)
    {
        bool dragging = false;
        Windows.Foundation.Point start = default;
        Sdk.Point graphStart = default;

        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
            {
                return;
            }

            dragging = true;
            start = e.GetCurrentPoint(RootGrid).Position;
            graphStart = DecoratorLocation(decorator);
            card.CapturePointer(e.Pointer);
            e.Handled = true;
        };
        card.PointerMoved += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            var current = e.GetCurrentPoint(RootGrid).Position;
            double zoom = CanvasTransform.ScaleX;
            if (zoom <= 0)
            {
                return;
            }

            double dx = (current.X - start.X) / zoom;
            double dy = (current.Y - start.Y) / zoom;
            SetDecoratorLocation(decorator, UnoPointProjection.ToSdk(graphStart.X + dx, graphStart.Y + dy));

            if (card.Parent is ContentPresenter container)
            {
                PositionDecorator(container, decorator);
            }

            e.Handled = true;
        };
        card.PointerReleased += (_, e) =>
        {
            dragging = false;
            card.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        };
    }

    private static void SetDecoratorLocation(object decorator, Sdk.Point value)
    {
        switch (decorator)
        {
            case GroupViewModel g: g.Location = value; break;
            case AnnotationViewModel a: a.Location = value; break;
        }
    }

    // ── Spotlight: añadir nodo por teclado; el núcleo filtra, el host pinta y confirma ──

    private void ShowSpotlight()
    {
        SpotlightPopup.Visibility = Visibility.Visible;
        SpotlightList.ItemsSource = _editor?.FilteredSpotlightItems;
        SpotlightSearchBox.Text = _editor?.SpotlightSearchText ?? string.Empty;
        _ = DispatcherQueue.TryEnqueue(() => SpotlightSearchBox.Focus(FocusState.Programmatic));
    }

    private void HideSpotlight()
    {
        SpotlightPopup.Visibility = Visibility.Collapsed;
        SpotlightList.ItemsSource = null;
    }

    private void OnSpotlightBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_editor is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Down:
                StepSpotlightSelection(+1);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Up:
                StepSpotlightSelection(-1);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter:
                ConfirmSpotlight();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Escape:
                _editor.CloseSpotlight();
                e.Handled = true;
                break;
            default:
                // El texto llega al VM por el binding TwoWay; el filtro se recalcula allí.
                break;
        }
    }

    private void OnSpotlightListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_editor is null)
        {
            return;
        }

        if (e.Key is Windows.System.VirtualKey.Enter)
        {
            ConfirmSpotlight();
            e.Handled = true;
        }
        else if (e.Key is Windows.System.VirtualKey.Escape)
        {
            _editor.CloseSpotlight();
            e.Handled = true;
        }
    }

    private void OnSpotlightListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        ConfirmSpotlight();
    }

    private void StepSpotlightSelection(int delta)
    {
        if (_editor is null || _editor.FilteredSpotlightItems.Count == 0)
        {
            return;
        }

        var items = _editor.FilteredSpotlightItems;
        int index = _editor.SelectedSpotlightItem is { } current ? items.IndexOf(current) : -1;
        index = (index + delta + items.Count) % items.Count;
        _editor.SelectedSpotlightItem = items[index];
        SpotlightList.SelectedItem = items[index];
    }

    /// <summary>Confirma la selección del spotlight: AddNode en el punto del grafo guardado al abrir.</summary>
    private void ConfirmSpotlight()
    {
        if (_editor?.SelectedSpotlightItem is not { } item || _editor.IsSpotlightOpen is false)
        {
            return;
        }

        var position = _editor.SpotlightCanvasPosition;
        _editor.AddNode(item.TypeName, position);
        _editor.CloseSpotlight();
        RebuildDecorators();
    }

    // ── Migas de subflujos ──

    private void RefreshBreadcrumbs()
    {
        if (_editor is null)
        {
            return;
        }

        BreadcrumbsHost.ItemsSource = _editor.Breadcrumbs;
        BreadcrumbsBar.Visibility = _editor.HasBreadcrumbs ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBreadcrumbClicked(object sender, RoutedEventArgs e)
    {
        if (_editor is null || (sender as FrameworkElement)?.DataContext is not BreadcrumbItem crumb)
        {
            return;
        }

        _editor.NavigateToBreadcrumbCommand.Execute(crumb);
        Rebuild();
        RebuildDecorators();
        RefreshBreadcrumbs();
    }
}
