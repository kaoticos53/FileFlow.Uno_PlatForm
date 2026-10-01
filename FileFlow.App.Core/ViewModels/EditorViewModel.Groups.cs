using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Services.UndoRedo;
using FileFlow.Sdk;

namespace FileFlow.App.ViewModels;

public partial class EditorViewModel
{
    public AnnotationViewModel AddAnnotation(Point? position = null, string title = "Nota", string content = "", string color = "#FEF08A")
    {
        var loc = position ?? new Point(Math.Max(50, -ViewportLocation.X + 150), Math.Max(50, -ViewportLocation.Y + 150));
        var annotation = new AnnotationViewModel(title, content, loc, color: color)
        {
            ParentEditor = this
        };
        Annotations.Add(annotation);
        CanvasDecorators.Add(annotation);
        _undoRedoService.Record(new AddAnnotationAction(this, annotation));
        return annotation;
    }

    [RelayCommand]
    public void AddNewAnnotation()
    {
        AddAnnotation();
    }

    [RelayCommand]
    public void DeleteAnnotation(AnnotationViewModel? annotation)
    {
        if (annotation != null)
        {
            Annotations.Remove(annotation);
            CanvasDecorators.Remove(annotation);
            _undoRedoService.Record(new DeleteAnnotationAction(this, annotation));
        }
    }

    public GroupViewModel AddGroup(Point? position = null, string title = "Grupo de Nodos", double width = 450, double height = 320, string color = "#3B82F6", IEnumerable<string>? nodeIds = null)
    {
        var loc = position ?? new Point(Math.Max(50, -ViewportLocation.X + 100), Math.Max(50, -ViewportLocation.Y + 100));
        var group = new GroupViewModel(title, loc, width, height, color, nodeIds)
        {
            ParentEditor = this
        };
        Groups.Add(group);
        CanvasDecorators.Insert(0, group);
        _undoRedoService.Record(new AddGroupAction(this, group));
        return group;
    }

    [RelayCommand]
    public void AddNewGroup()
    {
        AddGroup();
    }

    [RelayCommand]
    public void GroupSelectedNodes()
    {
        var selected = Nodes.Where(n => n.IsSelected).ToList();
        if (selected.Count == 0)
        {
            AddNewGroup();
            return;
        }

        double minX = selected.Min(n => n.Location.X) - 30;
        double minY = selected.Min(n => n.Location.Y) - 50;
        double maxX = selected.Max(n => n.Location.X + n.Width) + 30;
        double maxY = selected.Max(n => n.Location.Y + 250) + 30;

        AddGroup(new Point(minX, minY), "Grupo", Math.Max(300, maxX - minX), Math.Max(200, maxY - minY), "#3B82F6", selected.Select(n => n.Id));
    }

    [RelayCommand]
    public void DeleteGroup(GroupViewModel? group)
    {
        if (group != null)
        {
            Groups.Remove(group);
            CanvasDecorators.Remove(group);
            _undoRedoService.Record(new DeleteGroupAction(this, group));
        }
    }
}
