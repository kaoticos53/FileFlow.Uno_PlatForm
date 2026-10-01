using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Models;
using FileFlow.App.Services;
using FileFlow.App.Services.UndoRedo;
using FileFlow.Sdk;

namespace FileFlow.App.ViewModels;

public partial class EditorViewModel
{
    [RelayCommand]
    public void CopySelectedNodes(object? parameter = null)
    {
        var targets = ResolveTargetNodes(parameter);
        if (targets.Count > 0)
        {
            _clipboardService.Copy(targets, Connections);
        }
    }

    [RelayCommand]
    public void CutSelectedNodes(object? parameter = null)
    {
        var targets = ResolveTargetNodes(parameter);
        if (targets.Count > 0)
        {
            _clipboardService.Copy(targets, Connections);
            DeleteSelectedNodes(targets.Count == 1 ? targets[0] : null);
        }
    }

    [RelayCommand]
    public void PasteNodes(object? positionParam = null)
    {
        Point? targetPoint = null;
        if (positionParam is Point pt)
        {
            targetPoint = pt;
        }

        using var tx = _undoRedoService.BeginTransaction("Pegar Nodos");
        var result = _clipboardService.Paste(this, targetPoint);
        RecordPastedNodes(result.Nodes, "Pegar Nodos");
        AnnounceWhatCouldNotBeRebuilt(result.Report);
        if (result.Nodes.Count > 0)
        {
            SelectedNode = result.Nodes[^1];
        }
    }

    [RelayCommand]
    public void DuplicateSelectedNodes(object? parameter = null)
    {
        var targets = ResolveTargetNodes(parameter);
        if (targets.Count > 0)
        {
            using var tx = _undoRedoService.BeginTransaction("Duplicar Nodos");
            var result = _clipboardService.Duplicate(targets, Connections, this);
            RecordPastedNodes(result.Nodes, "Duplicar Nodos");
            AnnounceWhatCouldNotBeRebuilt(result.Report);
            if (result.Nodes.Count > 0)
            {
                SelectedNode = result.Nodes[^1];
            }
        }
    }

    /// <summary>
    /// Inscribe en el historial los nodos que acaban de entrar al lienzo por un pegado o una duplicación,
    /// como una sola acción.
    ///
    /// Sin esto el pegado no dejaba nada que deshacer por los nodos: los cables que reconstruye se
    /// registraban solos —pasan por <see cref="CreateConnection"/>—, pero los nodos entraban al lienzo sin
    /// registro, así que un Ctrl+Z tras un Ctrl+V deshacía <b>la acción anterior</b> y dejaba los nodos
    /// pegados donde estaban. Los cables no se repiten aquí: <see cref="AddNodesAction.Undo"/> retira
    /// además los que toquen a estos nodos.
    /// </summary>
    private void RecordPastedNodes(IReadOnlyList<NodeViewModel> pastedNodes, string description)
    {
        if (pastedNodes.Count > 0)
        {
            _undoRedoService.Record(new AddNodesAction(this, pastedNodes, description: description));
        }
    }

    /// <summary>
    /// Cuenta los cables que una reconstrucción no pudo rehacer —al abrir un flujo, al pegar y al duplicar— en
    /// el lienzo, que es donde está el grafo y donde se pueden arreglar, y en la consola, que es el registro
    /// que queda.
    ///
    /// El cartel lleva sólo la <b>cabecera</b> —qué pasó— y el detalle de cada cable vive en su fila, junto al
    /// botón que lo arregla. Antes el texto enumeraba los cables perdidos, y eso tiene un defecto que se ve en
    /// cuanto se arregla uno: la frase sigue contando lo que ya no es verdad, y reescribirla es llevar la
    /// cuenta en dos sitios.
    ///
    /// Un resultado sano <b>retira</b> el aviso anterior en vez de dejar el de la vez pasada: el aviso cuenta
    /// la última acción, y uno viejo sobre un grafo que ya no es el que se ve es una mentira.
    /// </summary>
    private void AnnounceWhatCouldNotBeRebuilt(ConnectionRebuildReport report)
    {
        if (report.IsComplete)
        {
            ClearCanvasNotice();
            return;
        }

        foreach (var connection in report.DroppedConnections)
        {
            LogLostConnection("LogDroppedConnection", "🔌 No se pudo reconstruir la conexión {0}: {1}", connection);
        }

        SetCanvasNotice(
            _loc.GetString("CanvasNoticeLostConnections", "🔌 No se pudieron reconstruir estas conexiones"),
            report.DroppedConnections);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // El arreglo de un cable perdido, en el propio aviso
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Convierte cada cable perdido en una fila que se puede pulsar: el nodo cuyo puerto falta, el puerto
    /// vigente que más se le parece y los dos botones.
    ///
    /// Sólo se ofrece lo que se puede cumplir. Hay fila si el nodo del puerto que falta está en el lienzo —a un
    /// nodo que no se pudo crear no se puede ir—; hay botón de reconectar si además el otro extremo existe o se
    /// le puede proponer un puerto; y hay propuesta si algún puerto vigente se parece lo suficiente como para no
    /// ser una trampa (ver <see cref="PortNameProposal"/>).
    /// </summary>
    private IReadOnlyList<DroppedConnectionFixViewModel> BuildFixes(IReadOnlyList<DroppedConnection> lostConnections)
    {
        var fixes = new List<DroppedConnectionFixViewModel>();
        var lookup = Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var connection in lostConnections)
        {
            var output = ResolveEnd(connection.Source, isOutput: true, lookup);
            var input = ResolveEnd(connection.Target, isOutput: false, lookup);

            // La fila se ancla en el primer extremo cuyo puerto falta: es el que hay que mirar. Si el cable no
            // perdió ningún puerto —el problema es un nodo que no está— no hay fila, porque no hay a dónde ir.
            if (output is not { Existing: null } && input is not { Existing: null })
            {
                continue;
            }

            var anchor = output is { Existing: null } ? output : input!;

            PortViewModel? proposedOutput = output is { Existing: null } ? ProposedPortFor(output) : null;
            PortViewModel? proposedInput = input is { Existing: null } ? ProposedPortFor(input) : null;
            PortViewModel? liveOutput = output?.Existing ?? proposedOutput;
            PortViewModel? liveInput = input?.Existing ?? proposedInput;
            bool canReconnect = liveOutput != null && liveInput != null;

            fixes.Add(new DroppedConnectionFixViewModel(
                anchor.Node,
                anchor.PortName,
                DroppedConnectionText.Describe(_loc, connection),
                (anchor.IsOutput ? proposedOutput : proposedInput)?.Name,
                node => FocusNode(node),
                ReconnectLostConnection,
                _loc)
            {
                CanReconnect = canReconnect,
                LiveEnds = canReconnect ? (liveOutput!, liveInput!) : null
            });
        }

        return fixes;
    }

    /// <summary>
    /// El extremo del cable tal y como está hoy en el lienzo: su nodo —si sigue ahí—, el puerto que nombraba y,
    /// si ese puerto existe, el puerto mismo. Un puerto que falta se queda sin resolver a propósito: proponer
    /// uno es decisión de <see cref="BuildFixes"/>, no de esta lectura.
    /// </summary>
    private static LostEnd? ResolveEnd(
        DroppedConnectionEnd end,
        bool isOutput,
        IReadOnlyDictionary<string, NodeViewModel> lookup)
    {
        if (!lookup.TryGetValue(end.NodeId, out var node))
        {
            return null;
        }

        var ports = isOutput ? node.OutputPorts : node.InputPorts;

        return new LostEnd(
            node,
            end.PortName,
            isOutput,
            ports.FirstOrDefault(port => port.Name.Equals(end.PortName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// El puerto vigente más parecido al que falta, de entre los que se pueden usar: una entrada que ya tiene
    /// cable no es candidata, porque reconectar ahí tiraría el cable que ya estaba y el usuario no lo pidió.
    /// </summary>
    private PortViewModel? ProposedPortFor(LostEnd end)
    {
        var candidates = (end.IsOutput ? end.Node.OutputPorts : end.Node.InputPorts)
            .Where(port => port.Direction == PortDirection.Output || Connections.All(connection => connection.Target != port))
            .ToList();

        string? proposed = PortNameProposal.Suggest(end.PortName, candidates.Select(port => port.Name));

        return proposed == null
            ? null
            : candidates.First(port => port.Name.Equals(proposed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Lleva la vista al nodo: lo selecciona y lo centra. Es la mitad del aviso que no depende de adivinar
    /// nada —el puerto que falta se ve al llegar— y la que queda cuando no hay nada que proponer.
    /// </summary>
    [RelayCommand]
    public void FocusNode(NodeViewModel? node)
    {
        if (node == null) return;

        foreach (var other in Nodes)
        {
            other.IsSelected = ReferenceEquals(other, node);
        }

        SelectedNode = node;
        BringToFront(node);
        ViewportLocation = EditorViewportCalculator.CenterOn(node, ViewportZoom);
    }

    /// <summary>
    /// Vuelve a trazar el cable perdido contra los puertos de la fila y retira esa fila. Si no se pudo trazar
    /// —el motor del lienzo rechaza una conexión por sus puertos— la fila <b>se queda</b>: quitarla sería decir
    /// que se arregló.
    /// </summary>
    public void ReconnectLostConnection(DroppedConnectionFixViewModel fix)
    {
        if (fix?.LiveEnds is not { } ends) return;

        CreateConnection(ends.Output, ends.Input);

        if (!Connections.Any(connection => connection.Source == ends.Output && connection.Target == ends.Input))
        {
            return;
        }

        CanvasNoticeFixes.Remove(fix);

        // Cuando ya no queda ningún cable perdido, el aviso tampoco: quedarse contando lo que ya está
        // arreglado es la misma mentira que un aviso viejo sobre otro grafo.
        //
        // Que no queden <b>filas</b> no es que no queden pérdidas: un cable cuyo nodo no está se pierde sin
        // ofrecer nada que pulsar, y retirar el aviso al arreglar el último arreglable escondería justo el
        // cable que el usuario no puede recuperar. El aviso se retira cuando ya no queda nada perdido.
        if (CanvasNoticeFixes.Count == 0 && _lostWithoutFixCount == 0)
        {
            ClearCanvasNotice();
        }
    }

    /// <summary>Un extremo del cable perdido, ya resuelto contra el lienzo.</summary>
    private sealed record LostEnd(NodeViewModel Node, string PortName, bool IsOutput, PortViewModel? Existing);
}
