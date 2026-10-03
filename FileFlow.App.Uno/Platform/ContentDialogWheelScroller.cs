using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FileFlow.App.Uno.Platform;

/// <summary>
/// Habilita el desplazamiento por rueda del ratón en una SUPERFICIE desplazable del host: un panel de la
/// ventana (consola, catálogo, ficha, ajustes, cajón del menú) o un diálogo modal <c>ContentDialog</c>.
///
/// <para><b>Hay un solo contrato</b>: <see cref="EnableScrollSurface"/>. El destino se resuelve por el PUNTO
/// del puntero (el <c>ScrollViewer</c> desplazable más profundo que lo contiene), no por el elemento de
/// origen, y el manejador cede el paso si un hijo ya aplicó la rueda. No hay manejadores ad-hoc por tipo de
/// superficie: el mismo motor sirve a la ventana y al modal.</para>
/// </summary>
internal static class ContentDialogWheelScroller
{
    /// <summary>
    /// Habilita la rueda del ratón sobre una superficie completa de la ventana (la raíz de un panel: la
    /// consola, el catálogo, la ficha). El manejador se engancha con <c>handledEventsToo</c> porque la rueda
    /// es un evento enrutado que los hijos —<c>ListView</c>, <c>TextBox</c>, <c>ComboBox</c> y el
    /// <c>ScrollViewer</c> interno de cualquiera de ellos— marcan como manejado antes de que suba al panel.
    ///
    /// <para><b>El destino se resuelve por el PUNTO del puntero, no por el <c>OriginalSource</c></b>: el origen
    /// cambia con el elemento que hay bajo el cursor (un <c>TextBlock</c>, un <c>TextBox</c>…), con la
    /// virtualización y con el reparto interno de cada plantilla, así que engancharse a él hacía que la rueda
    /// funcionara «en unas zonas y en otras no». Se busca el <c>ScrollViewer</c> desplazable más profundo que
    /// CONTIENE el punto, con el propio panel como último recurso.</para>
    /// </summary>
    public static void EnableScrollSurface(UIElement surface)
    {
        PointerEventHandler handler = (sender, e) =>
            OnScrollSurfacePointerWheelChanged(sender, e, surface);
        surface.AddHandler(UIElement.PointerWheelChangedEvent, handler, handledEventsToo: true);
    }

    /// <summary>El paso de una muesca de rueda, en píxeles lógicos (el mismo ritmo que los diálogos).</summary>
    private const double LinesStep = 48.0;

    private static void OnScrollSurfacePointerWheelChanged(
        object sender,
        PointerRoutedEventArgs e,
        UIElement surface)
    {
        // Si un manejador de más ABAJO (el ScrollViewer nativo del panel o de un hijo) ya aplicó la rueda,
        // no se repite: el doble desplazamiento es la mitad de la «erraticidad» que se veía.
        if (e.Handled)
        {
            return;
        }

        var point = e.GetCurrentPoint(surface);
        int delta = point.Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        if (ApplyWheelAtPoint(surface, point.Position, delta, point.Properties.IsHorizontalMouseWheel))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Aplica una muesca de rueda sobre la superficie y devuelve si movió algo. Es el MISMO código que
    /// decide el desplazamiento del evento real: el sondeo en runtime lo llama con un punto elegido para
    /// medir la resolución del destino y el movimiento, sin depender de que el entorno pueda inyectar la rueda.
    /// </summary>
    /// <param name="surface">La raíz del panel (el mismo objeto que <c>EnableScrollSurface</c> recibe).</param>
    /// <param name="point">El punto del puntero, en el marco de <paramref name="surface"/>.</param>
    /// <param name="delta">La muesca (<c>MouseWheelDelta</c>).</param>
    /// <param name="horizontal">Rueda horizontal (los botones laterales del ratón).</param>
    internal static bool ApplyWheelAtPoint(UIElement surface, Point point, int delta, bool horizontal)
    {
        if (delta == 0)
        {
            return false;
        }

        ScrollViewer? target = FindScrollViewerUnderPoint(surface, point);
        if (target is null)
        {
            return false;
        }

        if (horizontal)
        {
            if (target.ScrollableWidth <= 0)
            {
                return false;
            }

            double newOffset = Math.Clamp(target.HorizontalOffset + (delta / 120.0) * LinesStep,
                0, target.ScrollableWidth);
            if (Math.Abs(newOffset - target.HorizontalOffset) <= 0.1)
            {
                return false;
            }

            target.ChangeView(newOffset, null, null, disableAnimation: true);
            return true;
        }

        if (target.ScrollableHeight <= 0)
        {
            return false;
        }

        double verticalOffset = Math.Clamp(target.VerticalOffset - (delta / 120.0) * LinesStep,
            0, target.ScrollableHeight);
        if (Math.Abs(verticalOffset - target.VerticalOffset) <= 0.1)
        {
            return false;
        }

        target.ChangeView(null, verticalOffset, null, disableAnimation: true);
        return true;
    }

    /// <summary>El destino que la rueda elegiría en este punto de la superficie (para la sonda del sondeo).</summary>
    internal static ScrollViewer? ResolveTargetAtPoint(UIElement surface, Point point) =>
        FindScrollViewerUnderPoint(surface, point);

    /// <summary>
    /// El <c>ScrollViewer</c> desplazable que hay BAJO el punto: el más profundo que lo contiene y, si el
    /// punto cae en el relleno del panel (fuera de toda caja), el primero desplazable de la superficie.
    /// </summary>
    private static ScrollViewer? FindScrollViewerUnderPoint(UIElement surface, Point point)
    {
        return FindScrollableAtPoint(surface, surface, point)
            ?? FindScrollableDescendantScrollViewer(surface);
    }

    private static ScrollViewer? FindScrollableAtPoint(DependencyObject node, UIElement surface, Point point)
    {
        if (node is UIElement element && element.Visibility != Visibility.Visible)
        {
            return null;
        }

        if (!ContainsPoint(node, surface, point))
        {
            return null;
        }

        // Primero lo de dentro: si un viewer anidado contiene el punto, es él el que manda (la lección del
        // inspector, donde la pestaña de parámetros lleva su propio scroll dentro de la rejilla).
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            var nested = FindScrollableAtPoint(VisualTreeHelper.GetChild(node, i), surface, point);
            if (nested is not null)
            {
                return nested;
            }
        }

        return node is ScrollViewer scroll && (scroll.ScrollableHeight > 0 || scroll.ScrollableWidth > 0)
            ? scroll
            : null;
    }

    /// <summary>
    /// Si la caja de <paramref name="node"/> contiene el punto, ambos medidos en el marco de la superficie
    /// (el mismo sistema en el que <see cref="PointerRoutedEventArgs.GetCurrentPoint"/> lo entregó).
    /// </summary>
    private static bool ContainsPoint(DependencyObject node, UIElement surface, Point point)
    {
        if (node is not FrameworkElement element)
        {
            return false;
        }

        try
        {
            Rect bounds = element.TransformToVisual(surface).TransformBounds(
                new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return bounds.Contains(point);
        }
        catch
        {
            return false;
        }
    }

    private static ScrollViewer? FindScrollableDescendantScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll && (scroll.ScrollableHeight > 0 || scroll.ScrollableWidth > 0))
        {
            return scroll;
        }

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var found = FindScrollableDescendantScrollViewer(VisualTreeHelper.GetChild(parent, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

}
