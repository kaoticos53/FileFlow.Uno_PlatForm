using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Services.UndoRedo;
using FileFlow.Sdk;

namespace FileFlow.App.ViewModels;

public partial class EditorViewModel
{
    public void CreateConnection(PortViewModel source, PortViewModel target)
    {
        if (source == null || target == null || source == target) return;
        if (source.NodeOwner == target.NodeOwner) return;

        // Ensure Source is Output and Target is Input
        PortViewModel outputPort = source.Direction == PortDirection.Output ? source : target;
        PortViewModel inputPort = source.Direction == PortDirection.Output ? target : source;

        if (outputPort.Direction != PortDirection.Output || inputPort.Direction != PortDirection.Input)
            return;

        if (Connections.Any(c => c.Source == outputPort && c.Target == inputPort))
            return;

        using var tx = _undoRedoService.BeginTransaction($"Conectar {outputPort.NodeOwner.Title} -> {inputPort.NodeOwner.Title}");

        // Remove any existing connection to the same input port
        var existing = Connections.FirstOrDefault(c => c.Target == inputPort);
        if (existing != null)
        {
            Connections.Remove(existing);
            _undoRedoService.Record(new DeleteConnectionAction(this, existing));
        }

        var newConn = new ConnectionViewModel(outputPort, inputPort);
        Connections.Add(newConn);
        _undoRedoService.Record(new AddConnectionAction(this, newConn));
    }

    private static (PortViewModel? Source, PortViewModel? Target) ExtractPortsFromParameter(object? param)
    {
        if (param is PortViewModel singlePort)
        {
            return (null, singlePort);
        }

        if (param is System.Runtime.CompilerServices.ITuple tuple && tuple.Length > 0)
        {
            PortViewModel? p1 = tuple[0] as PortViewModel;
            PortViewModel? p2 = tuple.Length > 1 ? tuple[1] as PortViewModel : null;
            return (p1, p2);
        }

        return (null, null);
    }

    [RelayCommand]
    public void StartConnection(object? source)
    {
        var (p1, p2) = ExtractPortsFromParameter(source);
        var port = p1 ?? p2;
        if (port != null)
        {
            PendingConnection = new PendingConnectionViewModel(port);
            ApplyPortCompatibilityHighlight(port);
        }
    }

    [RelayCommand]
    public void FinishConnection(object? target)
    {
        var (p1, p2) = ExtractPortsFromParameter(target);
        PortViewModel? sourcePort = p1 ?? PendingConnection?.Source;
        PortViewModel? targetPort = p2;

        if (p1 != null && p2 == null)
        {
            if (PendingConnection?.Source != null && PendingConnection.Source != p1)
            {
                sourcePort = PendingConnection.Source;
                targetPort = p1;
            }
            else
            {
                targetPort = p1;
            }
        }

        if (sourcePort != null && targetPort != null && sourcePort != targetPort)
        {
            CreateConnection(sourcePort, targetPort);
        }
        if (PendingConnection != null)
        {
            PendingConnection.IsVisible = false;
        }
        PendingConnection = null;
        ClearPortCompatibilityHighlight();
    }

    [RelayCommand]
    public void CancelConnection()
    {
        if (PendingConnection != null)
        {
            PendingConnection.IsVisible = false;
        }
        PendingConnection = null;
        ClearPortCompatibilityHighlight();
    }

    /// <summary>
    /// Marca cada puerto del lienzo con su compatibilidad respecto al puerto que se está arrastrando, para
    /// que la tarjeta pueda resaltar los destinos válidos y atenuar el resto mientras se dibuja el cable.
    /// </summary>
    private void ApplyPortCompatibilityHighlight(PortViewModel source)
    {
        foreach (var port in AllPorts())
        {
            port.IsDragActive = true;
            port.ApplyDragHighlight(source);
        }
    }

    /// <summary>Devuelve todos los puertos del lienzo al estado de reposo (fin o cancelación del arrastre).</summary>
    public void ClearPortCompatibilityHighlight()
    {
        foreach (var port in AllPorts())
        {
            port.ClearDragHighlight();
        }
    }

    private IEnumerable<PortViewModel> AllPorts()
        => Nodes.SelectMany(n => n.InputPorts.Concat(n.OutputPorts));

    [RelayCommand]
    public void DisconnectConnector(object? connector)
    {
        var (p1, p2) = ExtractPortsFromParameter(connector);
        var port = p1 ?? p2;

        if (p1 != null && p2 != null)
        {
            var specificConn = Connections.FirstOrDefault(c => (c.Source == p1 && c.Target == p2) || (c.Source == p2 && c.Target == p1));
            if (specificConn != null)
            {
                Connections.Remove(specificConn);
                _undoRedoService.Record(new DeleteConnectionAction(this, specificConn));
                return;
            }
        }

        if (port != null)
        {
            var removeList = Connections.Where(c => c.Source == port || c.Target == port).ToList();
            if (removeList.Count > 0)
            {
                using var tx = _undoRedoService.BeginTransaction("Desconectar puerto");
                foreach (var conn in removeList)
                {
                    Connections.Remove(conn);
                    _undoRedoService.Record(new DeleteConnectionAction(this, conn));
                }
            }
        }
    }

    [RelayCommand]
    public void DeleteNode(object? nodeParam)
    {
        if (nodeParam is NodeViewModel node)
        {
            RemoveNodeWithConnections(node);
        }
    }

    [RelayCommand]
    public void DeleteConnection(object? connectionParam)
    {
        if (connectionParam is ConnectionViewModel conn)
        {
            Connections.Remove(conn);
            _undoRedoService.Record(new DeleteConnectionAction(this, conn));
        }
    }
}
