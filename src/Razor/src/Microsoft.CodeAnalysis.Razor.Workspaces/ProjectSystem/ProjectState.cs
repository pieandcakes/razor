﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Razor;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.ProjectEngineHost;
using Microsoft.AspNetCore.Razor.ProjectSystem;
using Microsoft.AspNetCore.Razor.Utilities;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.Razor.ProjectSystem;

// Internal tracker for DefaultProjectSnapshot
internal class ProjectState
{
    private const ProjectDifference ClearConfigurationVersionMask = ProjectDifference.ConfigurationChanged;

    private const ProjectDifference ClearProjectWorkspaceStateVersionMask =
        ProjectDifference.ConfigurationChanged |
        ProjectDifference.ProjectWorkspaceStateChanged;

    private const ProjectDifference ClearDocumentCollectionVersionMask =
        ProjectDifference.ConfigurationChanged |
        ProjectDifference.DocumentAdded |
        ProjectDifference.DocumentRemoved;

    private static readonly ImmutableDictionary<string, DocumentState> s_emptyDocuments = ImmutableDictionary.Create<string, DocumentState>(FilePathNormalizingComparer.Instance);
    private static readonly ImmutableDictionary<string, ImmutableArray<string>> s_emptyImportsToRelatedDocuments = ImmutableDictionary.Create<string, ImmutableArray<string>>(FilePathNormalizingComparer.Instance);
    private readonly object _lock;

    private readonly IProjectEngineFactoryProvider _projectEngineFactoryProvider;
    private RazorProjectEngine? _projectEngine;

    public static ProjectState Create(
        IProjectEngineFactoryProvider projectEngineFactoryProvider,
        HostProject hostProject,
        ProjectWorkspaceState projectWorkspaceState)
    {
        if (projectEngineFactoryProvider is null)
        {
            throw new ArgumentNullException(nameof(projectEngineFactoryProvider));
        }

        if (hostProject is null)
        {
            throw new ArgumentNullException(nameof(hostProject));
        }

        if (projectWorkspaceState is null)
        {
            throw new ArgumentNullException(nameof(projectWorkspaceState));
        }

        return new ProjectState(projectEngineFactoryProvider, hostProject, projectWorkspaceState);
    }

    private ProjectState(
        IProjectEngineFactoryProvider projectEngineFactoryProvider,
        HostProject hostProject,
        ProjectWorkspaceState projectWorkspaceState)
    {
        _projectEngineFactoryProvider = projectEngineFactoryProvider;
        HostProject = hostProject;
        ProjectWorkspaceState = projectWorkspaceState;
        Documents = s_emptyDocuments;
        ImportsToRelatedDocuments = s_emptyImportsToRelatedDocuments;
        Version = VersionStamp.Create();
        ProjectWorkspaceStateVersion = Version;
        DocumentCollectionVersion = Version;

        _lock = new object();
    }

    private ProjectState(
        ProjectState older,
        ProjectDifference difference,
        HostProject hostProject,
        ProjectWorkspaceState projectWorkspaceState,
        ImmutableDictionary<string, DocumentState> documents,
        ImmutableDictionary<string, ImmutableArray<string>> importsToRelatedDocuments)
    {
        if (older is null)
        {
            throw new ArgumentNullException(nameof(older));
        }

        if (hostProject is null)
        {
            throw new ArgumentNullException(nameof(hostProject));
        }

        if (documents is null)
        {
            throw new ArgumentNullException(nameof(documents));
        }

        if (importsToRelatedDocuments is null)
        {
            throw new ArgumentNullException(nameof(importsToRelatedDocuments));
        }

        if (projectWorkspaceState is null)
        {
            throw new ArgumentNullException(nameof(projectWorkspaceState));
        }

        _projectEngineFactoryProvider = older._projectEngineFactoryProvider;
        Version = older.Version.GetNewerVersion();

        HostProject = hostProject;
        ProjectWorkspaceState = projectWorkspaceState;
        Documents = documents;
        ImportsToRelatedDocuments = importsToRelatedDocuments;

        _lock = new object();

        if ((difference & ClearDocumentCollectionVersionMask) == 0)
        {
            // Document collection hasn't changed
            DocumentCollectionVersion = older.DocumentCollectionVersion;
        }
        else
        {
            DocumentCollectionVersion = Version;
        }

        if ((difference & ClearConfigurationVersionMask) == 0 && older._projectEngine != null)
        {
            // Optimistically cache the RazorProjectEngine.
            _projectEngine = older.ProjectEngine;
            ConfigurationVersion = older.ConfigurationVersion;
        }
        else
        {
            ConfigurationVersion = Version;
        }

        if ((difference & ClearProjectWorkspaceStateVersionMask) == 0 ||
            ProjectWorkspaceState == older.ProjectWorkspaceState ||
            ProjectWorkspaceState.Equals(older.ProjectWorkspaceState))
        {
            ProjectWorkspaceStateVersion = older.ProjectWorkspaceStateVersion;
        }
        else
        {
            ProjectWorkspaceStateVersion = Version;
        }

        if ((difference & ClearProjectWorkspaceStateVersionMask) != 0 &&
            CSharpLanguageVersion != older.CSharpLanguageVersion)
        {
            // C# language version changed. This impacts the ProjectEngine, reset it.
            _projectEngine = null;
            ConfigurationVersion = Version;
        }
    }

    // Internal set for testing.
    public ImmutableDictionary<string, DocumentState> Documents { get; internal set; }

    // Internal set for testing.
    public ImmutableDictionary<string, ImmutableArray<string>> ImportsToRelatedDocuments { get; internal set; }

    public HostProject HostProject { get; }

    public ProjectWorkspaceState ProjectWorkspaceState { get; }

    public ImmutableArray<TagHelperDescriptor> TagHelpers => ProjectWorkspaceState.TagHelpers;

    public LanguageVersion CSharpLanguageVersion => ProjectWorkspaceState.CSharpLanguageVersion;

    /// <summary>
    /// Gets the version of this project, INCLUDING content changes. The <see cref="Version"/> is
    /// incremented for each new <see cref="ProjectState"/> instance created.
    /// </summary>
    public VersionStamp Version { get; }

    /// <summary>
    /// Gets the version of this project, NOT INCLUDING computed or content changes. The
    /// <see cref="DocumentCollectionVersion"/> is incremented each time the configuration changes or
    /// a document is added or removed.
    /// </summary>
    public VersionStamp DocumentCollectionVersion { get; }

    public RazorProjectEngine ProjectEngine
    {
        get
        {
            lock (_lock)
            {
                _projectEngine ??= CreateProjectEngine();
            }

            return _projectEngine;

            RazorProjectEngine CreateProjectEngine()
            {
                var configuration = HostProject.Configuration;
                var rootDirectoryPath = Path.GetDirectoryName(HostProject.FilePath).AssumeNotNull();

                return _projectEngineFactoryProvider.Create(configuration, rootDirectoryPath, builder =>
                {
                    builder.SetRootNamespace(HostProject.RootNamespace);
                    builder.SetCSharpLanguageVersion(CSharpLanguageVersion);
                    builder.SetSupportLocalizedComponentNames();
                });
            }
        }
    }

    /// <summary>
    /// Gets the version of this project based on the project workspace state, NOT INCLUDING content
    /// changes. The computed state is guaranteed to change when the configuration or tag helpers
    /// change.
    /// </summary>
    public VersionStamp ProjectWorkspaceStateVersion { get; }

    public VersionStamp ConfigurationVersion { get; }

    public ProjectState WithAddedHostDocument(HostDocument hostDocument, TextLoader loader)
    {
        if (hostDocument is null)
        {
            throw new ArgumentNullException(nameof(hostDocument));
        }

        if (loader is null)
        {
            throw new ArgumentNullException(nameof(loader));
        }

        // Ignore attempts to 'add' a document with different data, we only
        // care about one, so it might as well be the one we have.
        if (Documents.ContainsKey(hostDocument.FilePath))
        {
            return this;
        }

        var documents = Documents.Add(hostDocument.FilePath, DocumentState.Create(hostDocument, version: 1, loader));

        // Compute the effect on the import map
        var importTargetPaths = GetImportDocumentTargetPaths(hostDocument);
        var importsToRelatedDocuments = AddToImportsToRelatedDocuments(ImportsToRelatedDocuments, hostDocument.FilePath, importTargetPaths);

        // Now check if the updated document is an import - it's important this this happens after
        // updating the imports map.
        if (importsToRelatedDocuments.TryGetValue(hostDocument.TargetPath, out var relatedDocuments))
        {
            foreach (var relatedDocument in relatedDocuments)
            {
                documents = documents.SetItem(relatedDocument, documents[relatedDocument].WithImportsChange());
            }
        }

        var state = new ProjectState(this, ProjectDifference.DocumentAdded, HostProject, ProjectWorkspaceState, documents, importsToRelatedDocuments);
        return state;
    }

    public ProjectState WithRemovedHostDocument(HostDocument hostDocument)
    {
        if (hostDocument is null)
        {
            throw new ArgumentNullException(nameof(hostDocument));
        }

        if (!Documents.ContainsKey(hostDocument.FilePath))
        {
            return this;
        }

        var documents = Documents.Remove(hostDocument.FilePath);

        // First check if the updated document is an import - it's important that this happens
        // before updating the imports map.
        if (ImportsToRelatedDocuments.TryGetValue(hostDocument.TargetPath, out var relatedDocuments))
        {
            foreach (var relatedDocument in relatedDocuments)
            {
                documents = documents.SetItem(relatedDocument, documents[relatedDocument].WithImportsChange());
            }
        }

        // Compute the effect on the import map
        var importTargetPaths = GetImportDocumentTargetPaths(hostDocument);
        var importsToRelatedDocuments = RemoveFromImportsToRelatedDocuments(ImportsToRelatedDocuments, hostDocument, importTargetPaths);

        var state = new ProjectState(this, ProjectDifference.DocumentRemoved, HostProject, ProjectWorkspaceState, documents, importsToRelatedDocuments);
        return state;
    }

    public ProjectState WithChangedHostDocument(HostDocument hostDocument, SourceText sourceText, VersionStamp textVersion)
    {
        if (hostDocument is null)
        {
            throw new ArgumentNullException(nameof(hostDocument));
        }

        if (!Documents.TryGetValue(hostDocument.FilePath, out var document))
        {
            return this;
        }

        var documents = Documents.SetItem(hostDocument.FilePath, document.WithText(sourceText, textVersion));

        if (ImportsToRelatedDocuments.TryGetValue(hostDocument.TargetPath, out var relatedDocuments))
        {
            foreach (var relatedDocument in relatedDocuments)
            {
                documents = documents.SetItem(relatedDocument, documents[relatedDocument].WithImportsChange());
            }
        }

        var state = new ProjectState(this, ProjectDifference.DocumentChanged, HostProject, ProjectWorkspaceState, documents, ImportsToRelatedDocuments);
        return state;
    }

    public ProjectState WithChangedHostDocument(HostDocument hostDocument, TextLoader loader)
    {
        if (hostDocument is null)
        {
            throw new ArgumentNullException(nameof(hostDocument));
        }

        if (!Documents.TryGetValue(hostDocument.FilePath, out var document))
        {
            return this;
        }

        var documents = Documents.SetItem(hostDocument.FilePath, document.WithTextLoader(loader));

        if (ImportsToRelatedDocuments.TryGetValue(hostDocument.TargetPath, out var relatedDocuments))
        {
            foreach (var relatedDocument in relatedDocuments)
            {
                documents = documents.SetItem(relatedDocument, documents[relatedDocument].WithImportsChange());
            }
        }

        var state = new ProjectState(this, ProjectDifference.DocumentChanged, HostProject, ProjectWorkspaceState, documents, ImportsToRelatedDocuments);
        return state;
    }

    public ProjectState WithHostProject(HostProject hostProject)
    {
        if (hostProject is null)
        {
            throw new ArgumentNullException(nameof(hostProject));
        }

        if (HostProject.Configuration.Equals(hostProject.Configuration) &&
            HostProject.RootNamespace == hostProject.RootNamespace)
        {
            return this;
        }

        var documents = Documents.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.WithConfigurationChange(), FilePathNormalizingComparer.Instance);

        // If the host project has changed then we need to recompute the imports map
        var importsToRelatedDocuments = s_emptyImportsToRelatedDocuments;

        foreach (var document in documents)
        {
            var importTargetPaths = GetImportDocumentTargetPaths(document.Value.HostDocument);
            importsToRelatedDocuments = AddToImportsToRelatedDocuments(importsToRelatedDocuments, document.Value.HostDocument.FilePath, importTargetPaths);
        }

        var state = new ProjectState(this, ProjectDifference.ConfigurationChanged, hostProject, ProjectWorkspaceState, documents, importsToRelatedDocuments);
        return state;
    }

    public ProjectState WithProjectWorkspaceState(ProjectWorkspaceState projectWorkspaceState)
    {
        if (ProjectWorkspaceState == projectWorkspaceState)
        {
            return this;
        }

        if (ProjectWorkspaceState.Equals(projectWorkspaceState))
        {
            return this;
        }

        var difference = ProjectDifference.ProjectWorkspaceStateChanged;
        var documents = Documents.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.WithProjectWorkspaceStateChange(), FilePathNormalizingComparer.Instance);
        var state = new ProjectState(this, difference, HostProject, projectWorkspaceState, documents, ImportsToRelatedDocuments);
        return state;
    }

    internal static ImmutableDictionary<string, ImmutableArray<string>> AddToImportsToRelatedDocuments(
        ImmutableDictionary<string, ImmutableArray<string>> importsToRelatedDocuments,
        string documentFilePath,
        List<string> importTargetPaths)
    {
        foreach (var importTargetPath in importTargetPaths)
        {
            if (!importsToRelatedDocuments.TryGetValue(importTargetPath, out var relatedDocuments))
            {
                relatedDocuments = ImmutableArray.Create<string>();
            }

            relatedDocuments = relatedDocuments.Add(documentFilePath);
            importsToRelatedDocuments = importsToRelatedDocuments.SetItem(importTargetPath, relatedDocuments);
        }

        return importsToRelatedDocuments;
    }

    private static ImmutableDictionary<string, ImmutableArray<string>> RemoveFromImportsToRelatedDocuments(
        ImmutableDictionary<string, ImmutableArray<string>> importsToRelatedDocuments,
        HostDocument hostDocument,
        List<string> importTargetPaths)
    {
        foreach (var importTargetPath in importTargetPaths)
        {
            if (importsToRelatedDocuments.TryGetValue(importTargetPath, out var relatedDocuments))
            {
                relatedDocuments = relatedDocuments.Remove(hostDocument.FilePath);
                importsToRelatedDocuments = relatedDocuments.Length > 0
                    ? importsToRelatedDocuments.SetItem(importTargetPath, relatedDocuments)
                    : importsToRelatedDocuments.Remove(importTargetPath);
            }
        }

        return importsToRelatedDocuments;
    }

    public List<string> GetImportDocumentTargetPaths(HostDocument hostDocument)
    {
        return GetImportDocumentTargetPaths(hostDocument.TargetPath, hostDocument.FileKind, ProjectEngine);
    }

    internal static List<string> GetImportDocumentTargetPaths(string targetPath, string fileKind, RazorProjectEngine projectEngine)
    {
        var importFeatures = projectEngine.ProjectFeatures.OfType<IImportProjectFeature>();
        var projectItem = projectEngine.FileSystem.GetItem(targetPath, fileKind);
        var importItems = importFeatures.SelectMany(f => f.GetImports(projectItem)).Where(i => i.FilePath != null);

        // Target path looks like `Foo\\Bar.cshtml`
        var targetPaths = new List<string>();
        foreach (var importItem in importItems)
        {
            var itemTargetPath = importItem.FilePath.Replace('/', '\\').TrimStart('\\');

            if (FilePathNormalizingComparer.Instance.Equals(itemTargetPath, targetPath))
            {
                // We've normalized the original importItem.FilePath into the HostDocument.TargetPath. For instance, if the HostDocument.TargetPath
                // was '/_Imports.razor' it'd be normalized down into '_Imports.razor'. The purpose of this method is to get the associated document
                // paths for a given import file (_Imports.razor / _ViewImports.cshtml); therefore, an import importing itself doesn't make sense.
                continue;
            }

            targetPaths.Add(itemTargetPath);
        }

        return targetPaths;
    }
}
