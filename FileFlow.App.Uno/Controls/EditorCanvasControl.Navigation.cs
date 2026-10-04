using System;
using System.Collections.Generic;
using System.Linq;
using FileFlow.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace FileFlow.App.Uno.Controls;

public sealed partial class EditorCanvasControl
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Diagnósticos y rastreo de foco
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// La cadena del origen de un evento, con el <c>DataContext</c> de cada eslabón (hito 278): es lo que
    /// identifica al elemento que se quedó con una pulsación cuando la conducta no es la esperada — así se
    /// midió que el pulsado del puerto caía en la cara de la tarjeta o en el fondo del lienzo. Va al rastro de
    /// sesión, junto al resto del instrumento del foco.
    /// </summary>
    private static string DescribeChain(object? source)
    {
        var parts = new List<string>();
        DependencyObject? node = source as DependencyObject;
        for (int i = 0; node is not null && i < 14; i++)
        {
            var fe = node as FrameworkElement;
            parts.Add($"{node.GetType().Name}#{fe?.Name ?? "-"}[dc={fe?.DataContext?.GetType().Name ?? "null"}]");
            node = node is UIElement ui ? VisualTreeHelper.GetParent(ui) : null;
        }

        return string.Join(" <- ", parts);
    }

    /// <summary>Quién tiene el foco, en palabras (tipo, nombre y cadena de ancestros), para el rastro del
    /// hito 252. La cadena es lo que identifica al ladrón cuando el que recibe el foco es anónimo (un
    /// <c>ScrollViewer</c> del inspector): con ella el rastro dice a qué panel pertenece.</summary>
    private string DescribeFocused() => CanvasFocusTrace.Describe(
        XamlRoot is { } xr ? Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xr) : null, ancestors: 5);

    /// <summary>
    /// La ficha del elemento que tiene el foco, para identificarlo cuando NO está en el árbol visual: tipo y
    /// nombre, si está cargado, lo que mide, su padre LÓGICO (que existe aunque el visual no) y, si es un
    /// <c>ScrollViewer</c>, qué lleva dentro y si ese contenido está cargado. Nació en el hito 253, porque
    /// «un <c>ScrollViewer</c> anónimo» no es una identificación.
    /// </summary>
    private string DescribeThief()
    {
        object? focused = XamlRoot is { } xr ? Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xr) : null;
        if (focused is not FrameworkElement element)
        {
            return CanvasFocusTrace.Describe(focused, ancestors: 3);
        }

        var parts = new List<string>
        {
            $"{element.GetType().Name}#{element.Name}",
            $"cargado={element.IsLoaded}",
            $"mide={element.ActualWidth:F0}x{element.ActualHeight:F0}",
            $"padreLogico={CanvasFocusTrace.Describe(element.Parent, ancestors: 2)}",
            element.XamlRoot is null ? "sin XamlRoot" : "con XamlRoot"
        };

        parts.Add("datacontext=" + DescribeDataContext(element));

        if (element is ScrollViewer scroll)
        {
            parts.Add("contenido=" + CanvasFocusTrace.Describe(scroll.Content, ancestors: 2));
            parts.Add("contenidoCargado="
                + (scroll.Content is FrameworkElement inner ? inner.IsLoaded.ToString() : "n/a"));

            // Un envoltorio de scroll suele llevar dentro un Border con el panel de verdad: el tipo y el
            // DataContext de su HIJO es lo que dice a qué parte de la aplicación pertenece el ladrón.
            if (scroll.Content is FrameworkElement wrapper)
            {
                parts.Add("hijo=" + CanvasFocusTrace.Describe(
                    wrapper is ContentControl cc ? cc.Content : null, ancestors: 1));
                parts.Add("hijoDatacontext=" + DescribeDataContext(wrapper));
            }
        }

        return string.Join(" | ", parts);
    }

    /// <summary>El tipo del DataContext de un elemento: lo que ata un ladrón anónimo a su panel.</summary>
    private static string DescribeDataContext(FrameworkElement? element) =>
        element?.DataContext?.GetType().Name ?? "sin DataContext";

    /// <summary>
    /// Los popups abiertos en este momento, con su contenido y su cadena. Existe por el ladrón del foco del
    /// 252: el <c>ScrollViewer</c> que se lleva el foco ~0,5 s después del clic NO tiene ancestros en el
    /// árbol visual de la ventana (por eso el rastro sólo imprime su tipo), y eso es la firma de un popup —
    /// un <c>ToolTip</c>, por ejemplo. Sin esta línea el ladrón queda como «un ScrollViewer anónimo».
    /// </summary>
    private string DescribeOpenPopups()
    {
        try
        {
            var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot);
            if (popups.Count == 0)
            {
                return "popups=ninguno";
            }

            var described = popups.Select(p => p.Child is null
                ? "sin contenido"
                : $"{p.Child.GetType().Name}[{CanvasFocusTrace.Describe(p.Child, ancestors: 4)}]");
            return "popups=" + string.Join(" + ", described);
        }
        catch (Exception ex)
        {
            return "popups=no se pudo consultar (" + ex.GetType().Name + ")";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Zoom, navegación del viewport y rejilla de fondo
    // ─────────────────────────────────────────────────────────────────────────────

    // La RUEDA ya no hace nada en el lienzo: el zoom por rueda se retiró en el hito 324 (el usuario reportó
    // que tampoco funcionaba y el host había quedado sin medida de rueda propia). El zoom se mueve con los
    // botones +/- de la barra y con ZoomBy, que es por donde lo miden la sonda del selfcheck y la suite.
    private void OnZoomIn(object sender, RoutedEventArgs e) => ZoomBy(1.1);

    private void OnZoomOut(object sender, RoutedEventArgs e) => ZoomBy(1 / 1.1);

    private void ZoomBy(double factor)
    {
        double zoom = Math.Clamp(CanvasTransform.ScaleX * factor, MinZoom, MaxZoom);
        CanvasTransform.ScaleX = zoom;
        CanvasTransform.ScaleY = zoom;
        ZoomText.Text = $"{Math.Round(zoom * 100)} %";
    }

    private void OnFitToScreen(object sender, RoutedEventArgs e)
    {
        if (_editor is null || _editor.Nodes.Count == 0)
        {
            return;
        }

        // El mismo calculador del núcleo que usa la versión anterior: un solo «ajustar a pantalla» para los dos hosts.
        var (zoom, location) = EditorViewportCalculator.CalculateFitToScreen(_editor.Nodes);

        CanvasTransform.ScaleX = zoom;
        CanvasTransform.ScaleY = zoom;
        CanvasTransform.TranslateX = -location.X * zoom;
        CanvasTransform.TranslateY = -location.Y * zoom;
        ZoomText.Text = $"{Math.Round(zoom * 100)} %";
    }

    private void DrawBackgroundGrid()
    {
        for (double x = 0; x <= 2400; x += GridStep)
        {
            GridLayer.Children.Add(new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = 2400,
                Stroke = CanvasBrush("CanvasGridBrush"),
                StrokeThickness = x % (GridStep * 2) == 0 ? 1 : 0.5
            });
        }

        for (double y = 0; y <= 2400; y += GridStep)
        {
            GridLayer.Children.Add(new Line
            {
                X1 = 0, Y1 = y, X2 = 2400, Y2 = y,
                Stroke = CanvasBrush("CanvasGridBrush"),
                StrokeThickness = y % (GridStep * 2) == 0 ? 1 : 0.5
            });
        }
    }
}
