using System.IO;
using FileFlow.App.Core;
using FileFlow.App.Preview.Core;
using FileFlow.Sdk;
using FluentAssertions;
using Xunit;

namespace FileFlow.Tests.Unit.App;

/// <summary>
/// Pruebas unitarias para el subsistema de vista previa de archivos: canal HostUi,
/// modelo de contexto <see cref="FilePreviewContext"/> y solicitud <see cref="FilePreviewRequest"/>.
/// </summary>
public class FilePreviewTests
{
    [Fact]
    public void HostUi_ShowFilePreview_ShouldTriggerFilePreviewRequestedEvent()
    {
        FilePreviewRequest? receivedRequest = null;
        Action<FilePreviewRequest> handler = req => receivedRequest = req;

        HostUi.FilePreviewRequested += handler;
        try
        {
            var ctx = new FilePreviewContext("test_file.png");
            var request = new FilePreviewRequest(ctx);

            HostUi.ShowFilePreview(request);

            receivedRequest.Should().NotBeNull();
            receivedRequest!.Context.Should().BeSameAs(ctx);
        }
        finally
        {
            HostUi.FilePreviewRequested -= handler;
        }
    }

    [Fact]
    public void FilePreviewContext_FromFileItemContext_ShouldCopyAllMetadataAndPaths()
    {
        var item = new FileItemContext("output.jpg")
        {
            OriginalPath = "input.jpg",
            FileSizeBytes = 2048
        };
        item.Metadata["Width"] = 1920;
        item.Metadata["Height"] = 1080;
        item.Metadata["DetectedObjects"] = "car, person";

        var previewCtx = FilePreviewContext.FromFileItemContext(item);

        previewCtx.CurrentPath.Should().Be("output.jpg");
        previewCtx.OriginalPath.Should().Be("input.jpg");
        previewCtx.FileName.Should().Be("output.jpg");
        previewCtx.Extension.Should().Be(".jpg");
        previewCtx.FileSizeBytes.Should().Be(2048);

        previewCtx.Metadata.Should().ContainKey("Width").WhoseValue.Should().Be(1920);
        previewCtx.Metadata.Should().ContainKey("Height").WhoseValue.Should().Be(1080);
        previewCtx.Metadata.Should().ContainKey("DetectedObjects").WhoseValue.Should().Be("car, person");
    }

    [Fact]
    public void FilePreviewContext_HasOriginalComparison_ShouldBeAccurate()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FileFlow_Preview_Test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            string originalFile = Path.Combine(tempDir, "orig.txt");
            string currentFile = Path.Combine(tempDir, "curr.txt");
            File.WriteAllText(originalFile, "hello original");
            File.WriteAllText(currentFile, "hello current");

            var ctxWithComparison = new FilePreviewContext(currentFile, originalFile);
            ctxWithComparison.HasOriginalComparison.Should().BeTrue();

            var ctxSamePath = new FilePreviewContext(currentFile, currentFile);
            ctxSamePath.HasOriginalComparison.Should().BeFalse();

            var ctxMissingOriginal = new FilePreviewContext(currentFile, Path.Combine(tempDir, "non_existent.txt"));
            ctxMissingOriginal.HasOriginalComparison.Should().BeFalse();

            var ctxNoOriginal = new FilePreviewContext(currentFile);
            ctxNoOriginal.HasOriginalComparison.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FilePreviewRequest_ShouldPreserveSiblingsAndOwner()
    {
        var ctx1 = new FilePreviewContext("file1.png");
        var ctx2 = new FilePreviewContext("file2.png");
        var siblings = new[] { ctx1, ctx2 };
        var owner = new object();

        var request = new FilePreviewRequest(ctx1, siblings, owner);

        request.Context.Should().BeSameAs(ctx1);
        request.Siblings.Should().BeEquivalentTo(siblings);
        request.Owner.Should().BeSameAs(owner);
    }
}
