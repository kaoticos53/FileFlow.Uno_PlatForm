using System;
using System.Collections.Generic;
using FileFlow.App.Uno.Controls;
using FileFlow.App.Uno.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileFlow.App.Uno;

/// <summary>
/// La medida de la RUEDA DEL RATÓN sobre las tres superficies del host que la consumen: la consola de
/// registros, el catálogo de nodos y la ficha del inspector (hito 319).
///
/// <para><b>El defecto que mide</b>: la rueda se resolvía por el <c>OriginalSource</c> —el elemento exacto
/// bajo el cursor—, así que funcionaba «en unas zonas y en otras no», y el inspector tenía tres manejadores
/// a la vez que se pisaban (el doble movimiento). El arreglo resuelve el destino por el PUNTO del puntero.
/// Lo que aquí se mide es justo eso: que sobre una zona de TEXTO el destino resuelto sea el viewer de la
/// superficie y que la muesca mueva su <c>VerticalOffset</c>.</para>
///
/// <para><b>Qué mide y qué no</b>: recorre el MISMO código que decide el evento real
/// (<c>ContentDialogWheelScroller.ApplyWheelAtPoint</c>), pero con un punto elegido por la sonda. No
/// sustituye a la rueda física: la inyección real la hace el observador externo
/// (<c>docs/qa/selfcheck_uia_probe.py</c>, con pywinauto), que es donde un ratón de verdad existe. Esta
/// sonda es la que corre SIEMPRE, en el sondeo interno, y la que evita que la resolución por punto se
/// rompa sin que nadie se entere.</para>
///
/// <para><b>Una superficie sin contenido desplazable no se declara rota</b>: con la lista vacía no hay
/// muesca que medir, así que esas superficies se cuentan como OMITIDAS y el sondeo las nombra aparte. Un
/// FALLO aquí significa que había contenido desplazable y la muesca no lo movió — que es el defecto real.</para>
///
/// <para><b>Quién afirma</b>: nadie por su cuenta — recibe el comprobador de quien la llama
/// (<c>SelfCheckCanvas</c>) y devuelve además las cuentas para que el sondeo escriba la nota de omisión.</para>
/// </summary>
internal static class SelfCheckWheel
{
    /// <summary>
    /// Corre las medidas de rueda sobre las tres superficies y devuelve (medidas, omitidas, detalle de las
    /// omitidas). Cada bloque con su try/catch: una medida que lance no puede llevarse el resto.
    /// </summary>
    internal static (int Measured, int Skipped, string Detail) Check(
        LogPanel? logs,
        NodeToolboxPanel? toolbox,
        NodeInspectorPanel? inspector,
        Action<bool, string> check)
    {
        var skipped = new List<string>();
        int measured = 0;

        foreach (var (name, surface) in new (string, UIElement?)[]
                 {
                     ("consola", logs?.WheelSurfaceForProbe),
                     ("catálogo", toolbox?.WheelSurfaceForProbe),
                     ("inspector", inspector?.WheelSurfaceForProbe)
                 })
        {
            if (surface is null)
            {
                check(false, $"rueda ({name}): la superficie no está montada en la ventana");
                continue;
            }

            try
            {
                if (Measure(name, surface, check))
                {
                    measured++;
                }
                else
                {
                    skipped.Add(name);
                }
            }
            catch (Exception ex)
            {
                check(false, $"rueda ({name}): la sonda lanzó {ex.GetType().Name}: {ex.Message}");
            }
        }

        return (measured, skipped.Count, skipped.Count == 0 ? string.Empty : string.Join(", ", skipped));
    }

    /// <summary>
    /// La medida de UNA superficie. Devuelve <c>false</c> si no había contenido desplazable que medir (una
    /// omisión, no un fallo) y <c>true</c> si midió de verdad.
    /// </summary>
    private static bool Measure(string name, UIElement surface, Action<bool, string> check)
    {
        var point = FindTextPoint(surface);
        if (point is null)
        {
            return false; // sin contenido desplazable: no hay nada que medir
        }

        ScrollViewer? target = ContentDialogWheelScroller.ResolveTargetAtPoint(surface, point.Value);
        if (target is null)
        {
            check(false, $"rueda ({name}): el punto sobre texto no resolvió ningún ScrollViewer");
            return true;
        }

        check(target.ScrollableHeight > 0,
            $"rueda ({name}): el destino resuelto sobre texto es un ScrollViewer desplazable "
            + $"(alto desplazable {target.ScrollableHeight:F0}px)");

        double before = target.VerticalOffset;
        bool moved = ContentDialogWheelScroller.ApplyWheelAtPoint(surface, point.Value, -120, horizontal: false);
        // `ChangeView` aplica en el siguiente pase de layout: sin forzarlo, la lectura inmediata devuelve el
        // offset VIEJO y la sonda declararía rota una rueda que sí movió (el falso negativo que se midió).
        surface.UpdateLayout();
        double after = target.VerticalOffset;
        check(moved && Math.Abs(after - before) > 0.1,
            $"rueda ({name}): la muesca sobre texto mueve el desplazamiento {before:F0} -> {after:F0}px");

        // La vuelta deja la superficie como estaba: el sondeo no puede dejar un panel a medias.
        ContentDialogWheelScroller.ApplyWheelAtPoint(surface, point.Value, 120, horizontal: false);
        surface.UpdateLayout();
        check(Math.Abs(target.VerticalOffset - before) < 0.51,
            $"rueda ({name}): la muesca inversa devuelve el desplazamiento a {before:F0}px "
            + $"(quedó en {target.VerticalOffset:F0}px)");

        return true;
    }

    /// <summary>
    /// Un punto, en el marco de la superficie, sobre contenido desplazable: se prefiere el primer
    /// <c>TextBlock</c> materializado (la «zona sobre texto» que el usuario reportaba como la que fallaba) y,
    /// si no hay ninguno, el centro del propio viewer desplazable (que también cae sobre su contenido).
    /// Devuelve <c>null</c> cuando ninguna zona resuelve un viewer desplazable.
    /// </summary>
    private static Windows.Foundation.Point? FindTextPoint(UIElement surface)
    {
        foreach (var node in Walk(surface))
        {
            if (node is not TextBlock { ActualWidth: > 0, ActualHeight: > 0 } block
                || block.Visibility != Visibility.Visible)
            {
                continue;
            }

            var point = CenterOf(block, surface, minInset: 4);
            if (point is not null)
            {
                return point;
            }
        }

        // Sin texto materializado: el centro del primer viewer desplazable de la superficie.
        foreach (var node in Walk(surface))
        {
            if (node is not ScrollViewer { ActualWidth: > 0, ActualHeight: > 0 } scroll
                || scroll.Visibility != Visibility.Visible
                || (scroll.ScrollableHeight <= 0 && scroll.ScrollableWidth <= 0))
            {
                continue;
            }

            var point = CenterOf(scroll, surface, minInset: 0);
            if (point is not null)
            {
                return point;
            }
        }

        return null;
    }

    /// <summary>El centro de la caja de un elemento, en el marco de la superficie, si resuelve un viewer.</summary>
    private static Windows.Foundation.Point? CenterOf(FrameworkElement element, UIElement surface, double minInset)
    {
        try
        {
            var box = element.TransformToVisual(surface).TransformBounds(
                new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            if (box.Width <= 0 || box.Height <= 0)
            {
                return null;
            }

            var point = new Windows.Foundation.Point(
                box.X + Math.Min(minInset, box.Width / 2),
                box.Y + box.Height / 2);

            return ContentDialogWheelScroller.ResolveTargetAtPoint(surface, point) is not null
                ? point
                : null;
        }
        catch
        {
            // Un elemento no materializado no tiene transformación: se salta y se prueba el siguiente.
            return null;
        }
    }

    /// <summary>
    /// El recorrido del árbol visual NO vive aquí: se le pide al cinturón compartido
    /// (<see cref="SelfCheckTree.Children"/>), que es la única casa del recorrido del sondeo.
    /// </summary>
    private static IEnumerable<object?> Walk(object? node)
    {
        foreach (var child in SelfCheckTree.Children(node))
        {
            yield return child;
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
