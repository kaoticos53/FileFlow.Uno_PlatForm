using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Services.UndoRedo;

namespace FileFlow.App.ViewModels;

public partial class EditorViewModel
{
    [ObservableProperty]
    private NodeViewModel? _selectedNode;

    private int _maxZIndex = 0;

    /// <summary>
    /// Los cables MARCADOS: la ÚNICA casa de la marca —el cable no lleva copia, así que no pueden
    /// divergir— y el estado del que dependen el resalte del lienzo y el atajo Supr. Es una COLECCIÓN porque
    /// con Ctrl se añaden cables a la marca (a diferencia del nodo, que lleva su propia bandera).
    /// </summary>
    public ObservableCollection<ConnectionViewModel> SelectedConnections { get; } = [];

    /// <summary>
    /// La REGLA DE SELECCIÓN del lienzo, en un solo sitio porque es una sola regla: <b>pulsar REEMPLAZA</b> —lo
    /// que estuviera elegido se suelta, nodos y cables— y <b>Ctrl AÑADE</b> a lo que ya estaba. Vale para el
    /// clic y para el rectángulo, y en los dos sentidos: elegir un nodo suelta los cables y elegir un cable
    /// suelta los nodos, porque la selección del lienzo es UNA (y Supr borra eso: lo que esté elegido).
    /// </summary>
    public void SelectNode(NodeViewModel node, bool add = false)
    {
        if (!add)
        {
            SelectedConnections.Clear();

            foreach (var other in Nodes.Where(n => n.IsSelected && !ReferenceEquals(n, node)))
            {
                other.IsSelected = false;
            }
        }

        node.IsSelected = true;
        SelectedNode = node;
        BringToFront(node);
    }

    /// <summary>
    /// Marca un cable (o lo añade con Ctrl). Marcar REEMPLAZA la selección entera —los cables que hubiera y
    /// los nodos—, y añadir deja intacto lo que ya estaba: la otra mitad de la misma regla.
    /// </summary>
    public void SelectConnection(ConnectionViewModel? connection, bool add = false)
    {
        if (!add)
        {
            SelectedConnections.Clear();
            DeselectAllNodes();
        }

        if (connection is not null && !SelectedConnections.Contains(connection))
        {
            SelectedConnections.Add(connection);
        }
    }

    /// <summary>El clic en el vacío: suelta TODO lo elegido (la selección del lienzo es una).</summary>
    public void ClearSelection()
    {
        SelectedConnections.Clear();
        DeselectAllNodes();
    }

    /// <summary>
    /// El RECTÁNGULO de selección (rubber band) elige un ÁREA: marca a la vez los <b>nodos</b> y los
    /// <b>cables</b> que quedaron dentro, con la misma regla del clic —<b>sin <paramref name="add"/> REEMPLAZA</b>
    /// y <b>con <paramref name="add"/> (Ctrl) AÑADE</b>—, donde lo que se conserva al añadir es lo que ya estaba
    /// elegido al empezar el rectángulo (<paramref name="baseNodes"/> / <paramref name="baseConnections"/>).
    ///
    /// <para><b>Por qué las dos cosas a la vez y no una</b>: el clic marca UNA —y elegir un cable suelta los
    /// nodos, porque la selección del lienzo es una—, pero el rectángulo no pulsa nada: dice «lo que cae aquí
    /// dentro está elegido», y lo que cae dentro puede ser un nodo, un cable o los dos. La marca de los cables
    /// sigue viviendo en <see cref="SelectedConnections"/> —una sola copia—, así que el resalte y el Supr no
    /// distinguen de dónde vino la marca.</para>
    /// </summary>
    public void ApplyRubberSelection(
        IReadOnlyCollection<NodeViewModel> nodesInside,
        IReadOnlyCollection<ConnectionViewModel> connectionsInside,
        bool add,
        IReadOnlyCollection<NodeViewModel> baseNodes,
        IReadOnlyCollection<ConnectionViewModel> baseConnections)
    {
        var wantedNodes = new HashSet<NodeViewModel>(nodesInside);
        var wantedConnections = new HashSet<ConnectionViewModel>(connectionsInside);
        if (add)
        {
            wantedNodes.UnionWith(baseNodes);
            wantedConnections.UnionWith(baseConnections);
        }

        NodeViewModel? last = null;
        foreach (var node in Nodes)
        {
            bool selected = wantedNodes.Contains(node);
            if (node.IsSelected != selected)
            {
                node.IsSelected = selected;
            }

            if (selected)
            {
                last = node;
            }
        }

        // El nodo de referencia (el que abre el inspector) es el último elegido del grafo; sin ninguno elegido,
        // ninguno. La misma decisión que el clic, pero sin «el que se pulsó»: aquí no se pulsó ninguno.
        SelectedNode = last;

        // Sólo se toca lo que CAMBIA: cada alta o baja de la colección repinta la capa de cables entera, y un
        // arrastre recalcula la selección en cada movimiento.
        foreach (var connection in SelectedConnections.ToList())
        {
            if (!wantedConnections.Contains(connection))
            {
                SelectedConnections.Remove(connection);
            }
        }

        foreach (var connection in Connections)
        {
            if (wantedConnections.Contains(connection) && !SelectedConnections.Contains(connection))
            {
                SelectedConnections.Add(connection);
            }
        }
    }

    private void DeselectAllNodes()
    {
        foreach (var node in Nodes.Where(n => n.IsSelected))
        {
            node.IsSelected = false;
        }

        SelectedNode = null;
    }

    public void BringToFront(NodeViewModel node)
    {
        if (node == null) return;
        if (node.ZIndex == _maxZIndex && _maxZIndex > 0) return;
        node.ZIndex = ++_maxZIndex;
    }

    private void OnNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is NodeViewModel nodeVm && e.PropertyName == nameof(NodeViewModel.IsSelected) && nodeVm.IsSelected)
        {
            SelectedNode = nodeVm;
            BringToFront(nodeVm);
        }
    }

    public void RemoveNodeWithConnections(NodeViewModel node)
    {
        if (node == null) return;
        var relatedConnections = Connections
            .Where(c => c.Source.NodeOwner == node || c.Target.NodeOwner == node)
            .ToList();

        foreach (var conn in relatedConnections)
        {
            Connections.Remove(conn);
        }

        node.PropertyChanged -= OnNodePropertyChanged;
        Nodes.Remove(node);
        _undoRedoService.Record(new DeleteNodesAction(this, [node], relatedConnections));
    }

    public List<NodeViewModel> ResolveTargetNodes(object? parameter)
    {
        if (parameter is NodeViewModel singleNode)
        {
            if (singleNode.IsSelected)
            {
                var selected = Nodes.Where(n => n.IsSelected).ToList();
                if (selected.Count > 1 && selected.Contains(singleNode))
                {
                    return selected;
                }
            }
            return [singleNode];
        }

        var targets = Nodes.Where(n => n.IsSelected).ToList();
        if (targets.Count == 0 && SelectedNode != null)
        {
            targets.Add(SelectedNode);
        }
        return targets;
    }

    /// <summary>
    /// Borra TODOS los cables marcados como UNA operación: cada cable deja su acción en el undo, dentro de
    /// una transacción, así que un solo Ctrl+Z los devuelve a todos (lo que el usuario espera de una selección
    /// que hizo de una vez).
    /// </summary>
    [RelayCommand]
    public void DeleteSelectedConnections(object? parameter = null)
    {
        var targets = SelectedConnections.ToList();
        if (targets.Count == 0) return;

        using var tx = _undoRedoService.BeginTransaction("Eliminar Conexiones");
        foreach (var connection in targets)
        {
            DeleteConnection(connection);
        }
    }

    [RelayCommand]
    public void DeleteSelectedNodes(object? parameter = null)
    {
        var targets = ResolveTargetNodes(parameter);
        if (targets.Count == 0) return;

        DeleteNodesWithTheirConnections(targets);
    }

    /// <summary>
    /// Supr borra LA SELECCIÓN ENTERA como UNA operación: los <b>cables marcados</b> y los <b>nodos elegidos</b>
    /// —con los cables que caen por ellos—, y un solo Ctrl+Z la devuelve entera. Antes eran dos caminos
    /// excluyentes: si había un cable marcado, el Supr se llevaba los cables y <b>dejaba los nodos</b>, así que
    /// una selección hecha de una vez (el rectángulo marca las dos cosas) había que borrarla en dos tandas y
    /// deshacerla otras dos.
    ///
    /// <para><b>Los cables que caen por sus nodos no se borran dos veces</b>: primero caen los nodos con sus
    /// cables (una sola acción del undo) y sólo se borran a mano los marcados que sigan en el grafo.</para>
    /// </summary>
    [RelayCommand]
    public void DeleteSelection(object? parameter = null)
    {
        var nodes = ResolveTargetNodes(parameter);
        var wires = SelectedConnections.ToList();
        if (nodes.Count == 0 && wires.Count == 0) return;

        using var tx = _undoRedoService.BeginTransaction("Eliminar Selección");

        if (nodes.Count > 0)
        {
            DeleteNodesWithTheirConnections(nodes);
            wires = wires.Where(Connections.Contains).ToList();
        }

        foreach (var connection in wires)
        {
            DeleteConnection(connection);
        }
    }

    /// <summary>
    /// La baja de un nodo con todo lo que cuelga de él, en UNA acción del undo (los nodos y sus conexiones).
    /// Es la mitad que comparten el borrado de nodos y el borrado de la selección entera.
    /// </summary>
    private void DeleteNodesWithTheirConnections(IReadOnlyCollection<NodeViewModel> targets)
    {
        var targetIds = new HashSet<string>(targets.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        var relatedConnections = Connections
            .Where(c => targetIds.Contains(c.Source.NodeOwner.Id) || targetIds.Contains(c.Target.NodeOwner.Id))
            .ToList();

        foreach (var conn in relatedConnections)
        {
            Connections.Remove(conn);
        }

        foreach (var node in targets)
        {
            node.PropertyChanged -= OnNodePropertyChanged;
            Nodes.Remove(node);
        }

        _undoRedoService.Record(new DeleteNodesAction(this, targets, relatedConnections));
    }
}
