﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.ProjectEngineHost;
using Microsoft.AspNetCore.Razor.Test.Common;
using Microsoft.AspNetCore.Razor.Test.Common.Editor;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
using Microsoft.CodeAnalysis.Razor.Workspaces;
using Microsoft.VisualStudio.Editor.Razor;
using Microsoft.VisualStudio.Editor.Razor.Settings;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.VisualStudio.LegacyEditor.Razor.Test;

public class RazorDocumentManagerTest : ProjectSnapshotManagerDispatcherTestBase
{
    private const string FilePath = "C:/Some/Path/TestDocumentTracker.cshtml";
    private const string ProjectPath = "C:/Some/Path/TestProject.csproj";

    private static readonly IContentType s_razorCoreContentType =
        StrictMock.Of<IContentType>(c =>
            c.IsOfType(RazorLanguage.CoreContentType) == true);

    private static readonly IContentType s_nonRazorCoreContentType =
        StrictMock.Of<IContentType>(c =>
            c.IsOfType(It.IsAny<string>()) == false);

    private readonly IProjectSnapshotManagerAccessor _projectManagerAccessor;
    private readonly IWorkspaceEditorSettings _workspaceEditorSettings;
    private readonly IImportDocumentManager _importDocumentManager;

    public RazorDocumentManagerTest(ITestOutputHelper testOutput)
        : base(testOutput)
    {
        var projectManagerMock = new StrictMock<ProjectSnapshotManagerBase>();
        projectManagerMock
            .Setup(p => p.GetAllProjectKeys(It.IsAny<string>()))
            .Returns(ImmutableArray<ProjectKey>.Empty);
        projectManagerMock
            .Setup(p => p.GetProjects())
            .Returns(ImmutableArray<IProjectSnapshot>.Empty);

        IProjectSnapshot? projectResult = null;
        projectManagerMock
            .Setup(p => p.TryGetLoadedProject(It.IsAny<ProjectKey>(), out projectResult))
            .Returns(false);

        var projectManager = projectManagerMock.Object;

        var projectManagerAccessorMock = new StrictMock<IProjectSnapshotManagerAccessor>();
        projectManagerAccessorMock
            .SetupGet(x => x.Instance)
            .Returns(projectManager);

        _projectManagerAccessor = projectManagerAccessorMock.Object;

        _workspaceEditorSettings = new WorkspaceEditorSettings(
            StrictMock.Of<IClientSettingsManager>());

        var importDocumentManager = new StrictMock<IImportDocumentManager>();
        importDocumentManager
            .Setup(m => m.OnSubscribed(It.IsAny<IVisualStudioDocumentTracker>()))
            .Verifiable();
        importDocumentManager
            .Setup(m => m.OnUnsubscribed(It.IsAny<IVisualStudioDocumentTracker>()))
            .Verifiable();
        _importDocumentManager = importDocumentManager.Object;
    }

    private static ITextBuffer CreateTextBuffer(bool core)
        => StrictMock.Of<ITextBuffer>(b =>
            b.ContentType == (core ? s_razorCoreContentType : s_nonRazorCoreContentType) &&
            b.Properties == new PropertyCollection());

    [UIFact]
    public async Task OnTextViewOpened_ForNonRazorTextBuffer_DoesNothing()
    {
        // Arrange
        var editorFactoryService = StrictMock.Of<IRazorEditorFactoryService>();
        var documentManager = new RazorDocumentManager(editorFactoryService, Dispatcher, JoinableTaskContext);
        var textView = StrictMock.Of<ITextView>();
        var nonCoreTextBuffer = CreateTextBuffer(core: false);

        // Act & Assert
        await documentManager.OnTextViewOpenedAsync(textView, [nonCoreTextBuffer]);
    }

    [UIFact]
    public async Task OnTextViewOpened_ForRazorTextBuffer_AddsTextViewToTracker()
    {
        // Arrange
        var textView = StrictMock.Of<ITextView>();
        var coreTextBuffer = CreateTextBuffer(core: true);

        IVisualStudioDocumentTracker? documentTracker = new VisualStudioDocumentTracker(
            Dispatcher,
            JoinableTaskContext,
            FilePath,
            ProjectPath,
            _projectManagerAccessor,
            _workspaceEditorSettings,
            ProjectEngineFactories.DefaultProvider,
            coreTextBuffer,
            _importDocumentManager);
        var editorFactoryService = StrictMock.Of<IRazorEditorFactoryService>(f =>
            f.TryGetDocumentTracker(coreTextBuffer, out documentTracker) == true);
        var documentManager = new RazorDocumentManager(editorFactoryService, Dispatcher, JoinableTaskContext);

        // Act
        await documentManager.OnTextViewOpenedAsync(textView, [coreTextBuffer]);

        // Assert
        Assert.Same(Assert.Single(documentTracker.TextViews), textView);
    }

    [UIFact]
    public async Task OnTextViewOpened_SubscribesAfterFirstTextViewOpened()
    {
        // Arrange
        var textView = StrictMock.Of<ITextView>();
        var coreTextBuffer = CreateTextBuffer(core: true);
        var nonCoreTextBuffer = CreateTextBuffer(core: false);

        IVisualStudioDocumentTracker? documentTracker = new VisualStudioDocumentTracker(
            Dispatcher,
            JoinableTaskContext,
            FilePath,
            ProjectPath,
            _projectManagerAccessor,
            _workspaceEditorSettings,
            ProjectEngineFactories.DefaultProvider,
            coreTextBuffer,
            _importDocumentManager);
        var editorFactoryService = StrictMock.Of<IRazorEditorFactoryService>(f =>
            f.TryGetDocumentTracker(It.IsAny<ITextBuffer>(), out documentTracker) == true);
        var documentManager = new RazorDocumentManager(editorFactoryService, Dispatcher, JoinableTaskContext);

        // Assert 1
        Assert.False(documentTracker.IsSupportedProject);

        // Act
        await documentManager.OnTextViewOpenedAsync(textView, [coreTextBuffer, nonCoreTextBuffer]);

        // Assert 2
        Assert.True(documentTracker.IsSupportedProject);
    }

    [UIFact]
    public async Task OnTextViewClosed_TextViewWithoutDocumentTracker_DoesNothing()
    {
        // Arrange
        var documentManager = new RazorDocumentManager(StrictMock.Of<IRazorEditorFactoryService>(), Dispatcher, JoinableTaskContext);
        var textView = StrictMock.Of<ITextView>();
        var coreTextBuffer = CreateTextBuffer(core: true);

        // Act
        await documentManager.OnTextViewClosedAsync(textView, [coreTextBuffer]);

        // Assert
        Assert.False(coreTextBuffer.Properties.ContainsProperty(typeof(IVisualStudioDocumentTracker)));
    }

    [UIFact]
    public async Task OnTextViewClosed_ForAnyTextBufferWithTracker_RemovesTextView()
    {
        // Arrange
        var textView1 = StrictMock.Of<ITextView>();
        var textView2 = StrictMock.Of<ITextView>();
        var coreTextBuffer = CreateTextBuffer(core: true);
        var nonCoreTextBuffer = CreateTextBuffer(core: false);

        // Preload the buffer's properties with a tracker, so it's like we've already tracked this one.
        var documentTracker = new VisualStudioDocumentTracker(
            Dispatcher,
            JoinableTaskContext,
            FilePath,
            ProjectPath,
            _projectManagerAccessor,
            _workspaceEditorSettings,
            ProjectEngineFactories.DefaultProvider,
            coreTextBuffer,
            _importDocumentManager);
        documentTracker.AddTextView(textView1);
        documentTracker.AddTextView(textView2);
        coreTextBuffer.Properties.AddProperty(typeof(IVisualStudioDocumentTracker), documentTracker);

        documentTracker = new VisualStudioDocumentTracker(
            Dispatcher, JoinableTaskContext, FilePath, ProjectPath, _projectManagerAccessor, _workspaceEditorSettings,
            ProjectEngineFactories.DefaultProvider, nonCoreTextBuffer, _importDocumentManager);
        documentTracker.AddTextView(textView1);
        documentTracker.AddTextView(textView2);
        nonCoreTextBuffer.Properties.AddProperty(typeof(IVisualStudioDocumentTracker), documentTracker);

        var editorFactoryService = StrictMock.Of<IRazorEditorFactoryService>();
        var documentManager = new RazorDocumentManager(editorFactoryService, Dispatcher, JoinableTaskContext);

        // Act
        await documentManager.OnTextViewClosedAsync(textView2, [coreTextBuffer, nonCoreTextBuffer]);

        // Assert
        documentTracker = coreTextBuffer.Properties.GetProperty<VisualStudioDocumentTracker>(typeof(IVisualStudioDocumentTracker));
        Assert.Same(Assert.Single(documentTracker.TextViews), textView1);

        documentTracker = nonCoreTextBuffer.Properties.GetProperty<VisualStudioDocumentTracker>(typeof(IVisualStudioDocumentTracker));
        Assert.Same(Assert.Single(documentTracker.TextViews), textView1);
    }

    [UIFact]
    public async Task OnTextViewClosed_UnsubscribesAfterLastTextViewClosed()
    {
        // Arrange
        var textView1 = StrictMock.Of<ITextView>();
        var textView2 = StrictMock.Of<ITextView>();
        var coreTextBuffer = CreateTextBuffer(core: true);
        var nonCoreTextBuffer = CreateTextBuffer(core: false);

        var documentTracker = new VisualStudioDocumentTracker(
            Dispatcher,
            JoinableTaskContext,
            FilePath,
            ProjectPath,
            _projectManagerAccessor,
            _workspaceEditorSettings,
            ProjectEngineFactories.DefaultProvider,
            coreTextBuffer,
            _importDocumentManager);

        coreTextBuffer.Properties.AddProperty(typeof(IVisualStudioDocumentTracker), documentTracker);
        var editorFactoryService = StrictMock.Of<IRazorEditorFactoryService>();
        var documentManager = new RazorDocumentManager(editorFactoryService, Dispatcher, JoinableTaskContext);

        // Populate the text views
        documentTracker.Subscribe();
        documentTracker.AddTextView(textView1);
        documentTracker.AddTextView(textView2);

        // Act 1
        await documentManager.OnTextViewClosedAsync(textView2, [coreTextBuffer, nonCoreTextBuffer]);

        // Assert 1
        Assert.True(documentTracker.IsSupportedProject);

        // Act
        await documentManager.OnTextViewClosedAsync(textView1, [coreTextBuffer, nonCoreTextBuffer]);

        // Assert 2
        Assert.False(documentTracker.IsSupportedProject);
    }
}
