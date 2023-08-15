﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Composition;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.LanguageServer.Extensions;
using Microsoft.AspNetCore.Razor.LanguageServer.Protocol;
using Microsoft.AspNetCore.Razor.Telemetry;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
using Microsoft.CodeAnalysis.Razor.Workspaces;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Editor.Razor;
using Microsoft.VisualStudio.Editor.Razor.Logging;
using Microsoft.VisualStudio.LanguageServer.ContainedLanguage;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Threading;
using static Microsoft.VisualStudio.LanguageServer.ContainedLanguage.DefaultLSPDocumentSynchronizer;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor;

[Export(typeof(RazorCustomMessageTarget))]
internal partial class RazorCustomMessageTarget
{
    private readonly TrackingLSPDocumentManager _documentManager;
    private readonly JoinableTaskFactory _joinableTaskFactory;
    private readonly LSPRequestInvoker _requestInvoker;
    private readonly ITelemetryReporter _telemetryReporter;
    private readonly LanguageServerFeatureOptions _languageServerFeatureOptions;
    private readonly FormattingOptionsProvider _formattingOptionsProvider;
    private readonly IClientSettingsManager _editorSettingsManager;
    private readonly LSPDocumentSynchronizer _documentSynchronizer;
    private readonly CSharpVirtualDocumentAddListener _csharpVirtualDocumentAddListener;
    private readonly IOutputWindowLogger? _outputWindowLogger;

    [ImportingConstructor]
    public RazorCustomMessageTarget(
        LSPDocumentManager documentManager,
        JoinableTaskContext joinableTaskContext,
        LSPRequestInvoker requestInvoker,
        FormattingOptionsProvider formattingOptionsProvider,
        IClientSettingsManager editorSettingsManager,
        LSPDocumentSynchronizer documentSynchronizer,
        CSharpVirtualDocumentAddListener csharpVirtualDocumentAddListener,
        ITelemetryReporter telemetryReporter,
        LanguageServerFeatureOptions languageServerFeatureOptions,
        [Import(AllowDefault = true)] IOutputWindowLogger? outputWindowLogger)
    {
        if (documentManager is null)
        {
            throw new ArgumentNullException(nameof(documentManager));
        }

        if (joinableTaskContext is null)
        {
            throw new ArgumentNullException(nameof(joinableTaskContext));
        }

        if (requestInvoker is null)
        {
            throw new ArgumentNullException(nameof(requestInvoker));
        }

        if (formattingOptionsProvider is null)
        {
            throw new ArgumentNullException(nameof(formattingOptionsProvider));
        }

        if (editorSettingsManager is null)
        {
            throw new ArgumentNullException(nameof(editorSettingsManager));
        }

        if (documentSynchronizer is null)
        {
            throw new ArgumentNullException(nameof(documentSynchronizer));
        }

        if (csharpVirtualDocumentAddListener is null)
        {
            throw new ArgumentNullException(nameof(csharpVirtualDocumentAddListener));
        }

        _documentManager = (TrackingLSPDocumentManager)documentManager;

        if (_documentManager is null)
        {
            throw new ArgumentException("The LSP document manager should be of type " + typeof(TrackingLSPDocumentManager).FullName, nameof(_documentManager));
        }

        if (telemetryReporter is null)
        {
            throw new ArgumentNullException(nameof(telemetryReporter));
        }

        if (languageServerFeatureOptions is null)
        {
            throw new ArgumentNullException(nameof(languageServerFeatureOptions));
        }

        _joinableTaskFactory = joinableTaskContext.Factory;

        _requestInvoker = requestInvoker;
        _formattingOptionsProvider = formattingOptionsProvider;
        _editorSettingsManager = editorSettingsManager;
        _documentSynchronizer = documentSynchronizer;
        _csharpVirtualDocumentAddListener = csharpVirtualDocumentAddListener;
        _telemetryReporter = telemetryReporter;
        _languageServerFeatureOptions = languageServerFeatureOptions;
        _outputWindowLogger = outputWindowLogger;
    }

    private async Task<DelegationRequestDetails?> GetProjectedRequestDetailsAsync(IDelegatedParams request, CancellationToken cancellationToken)
    {
        string languageServerName;

        bool synchronized;
        VirtualDocumentSnapshot virtualDocumentSnapshot;
        if (request.ProjectedKind == RazorLanguageKind.Html)
        {
            (synchronized, virtualDocumentSnapshot) = await TrySynchronizeVirtualDocumentAsync<HtmlVirtualDocumentSnapshot>(
                request.Identifier.Version,
                request.Identifier.TextDocumentIdentifier,
                cancellationToken,
                rejectOnNewerParallelRequest: false);
            languageServerName = RazorLSPConstants.HtmlLanguageServerName;
        }
        else if (request.ProjectedKind == RazorLanguageKind.CSharp)
        {
            (synchronized, virtualDocumentSnapshot) = await TrySynchronizeVirtualDocumentAsync<CSharpVirtualDocumentSnapshot>(
                request.Identifier.Version,
                request.Identifier.TextDocumentIdentifier,
                cancellationToken,
                rejectOnNewerParallelRequest: false);
            languageServerName = RazorLSPConstants.RazorCSharpLanguageServerName;
        }
        else
        {
            Debug.Fail("Unexpected RazorLanguageKind. This shouldn't really happen in a real scenario.");
            return null;
        }

        if (!synchronized)
        {
            return null;
        }

        return new DelegationRequestDetails(languageServerName, virtualDocumentSnapshot.Uri, virtualDocumentSnapshot.Snapshot.TextBuffer);
    }

    private record struct DelegationRequestDetails(string LanguageServerName, Uri ProjectedUri, ITextBuffer TextBuffer);

    private async Task<SynchronizedResult<TVirtualDocumentSnapshot>> TrySynchronizeVirtualDocumentAsync<TVirtualDocumentSnapshot>(
       int requiredHostDocumentVersion,
       TextDocumentIdentifier hostDocument,
       CancellationToken cancellationToken,
       bool rejectOnNewerParallelRequest = true)
       where TVirtualDocumentSnapshot : VirtualDocumentSnapshot
    {
        // For Html documents we don't do anything fancy, just call the standard service
        // If we're not generating unique document file names, then we can treat C# documents the same way
        if (!_languageServerFeatureOptions.IncludeProjectKeyInGeneratedFilePath ||
            typeof(TVirtualDocumentSnapshot) == typeof(HtmlVirtualDocumentSnapshot))
        {
            return await _documentSynchronizer.TrySynchronizeVirtualDocumentAsync<TVirtualDocumentSnapshot>(requiredHostDocumentVersion, hostDocument.Uri, cancellationToken).ConfigureAwait(false);
        }

        var virtualDocument = FindVirtualDocument<TVirtualDocumentSnapshot>(hostDocument.Uri, hostDocument.GetProjectContext());

        if (virtualDocument is { ProjectKey.Id: null })
        {
            _outputWindowLogger?.LogDebug("Trying to sync to a doc with no project Id. Waiting 500ms for document add.");
            if (await _csharpVirtualDocumentAddListener.WaitForDocumentAddAsync(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false))
            {
                _outputWindowLogger?.LogDebug("Wait successful!");
                virtualDocument = FindVirtualDocument<TVirtualDocumentSnapshot>(hostDocument.Uri, hostDocument.GetProjectContext());
            }
            else
            {
                _outputWindowLogger?.LogDebug("Timed out :(");
            }
        }

        if (virtualDocument is null)
        {
            _outputWindowLogger?.LogDebug("No virtual document found, falling back to old code.");
            return await _documentSynchronizer.TrySynchronizeVirtualDocumentAsync<TVirtualDocumentSnapshot>(requiredHostDocumentVersion, hostDocument.Uri, cancellationToken).ConfigureAwait(false);
        }

        var result = await _documentSynchronizer.TrySynchronizeVirtualDocumentAsync<TVirtualDocumentSnapshot>(requiredHostDocumentVersion, hostDocument.Uri, virtualDocument.Uri, rejectOnNewerParallelRequest, cancellationToken).ConfigureAwait(false);

        // If we failed to sync on version 1, then it could be that we got new information while waiting, so try again
        if (requiredHostDocumentVersion == 1 && !result.Synchronized)
        {
            _outputWindowLogger?.LogDebug("Sync failed for v1 document. Trying again");
            virtualDocument = FindVirtualDocument<TVirtualDocumentSnapshot>(hostDocument.Uri, hostDocument.GetProjectContext());

            if (virtualDocument is null)
            {
                _outputWindowLogger?.LogDebug("No virtual document found, falling back to old code.");
                return await _documentSynchronizer.TrySynchronizeVirtualDocumentAsync<TVirtualDocumentSnapshot>(requiredHostDocumentVersion, hostDocument.Uri, cancellationToken).ConfigureAwait(false);
            }

            _outputWindowLogger?.LogDebug("Got virtual document after trying again {uri}.Trying again.", virtualDocument.Uri);

            // try again
            result = await _documentSynchronizer.TrySynchronizeVirtualDocumentAsync<TVirtualDocumentSnapshot>(requiredHostDocumentVersion, hostDocument.Uri, virtualDocument.Uri, rejectOnNewerParallelRequest, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private SynchronizedResult<TVirtualDocumentSnapshot>? TryReturnPossiblyFutureSnapshot<TVirtualDocumentSnapshot>(
        int requiredHostDocumentVersion,
        TextDocumentIdentifier hostDocument) where TVirtualDocumentSnapshot : VirtualDocumentSnapshot
    {
        if (_documentSynchronizer is not DefaultLSPDocumentSynchronizer documentSynchronizer)
        {
            Debug.Fail("Got an LSP document synchronizer I don't know how to handle.");
            throw new InvalidOperationException("Got an LSP document synchronizer I don't know how to handle.");
        }

        // If we're not generating unique document file names, then we don't need to ensure we find the right virtual document
        // as there can only be one anyway
        if (_languageServerFeatureOptions.IncludeProjectKeyInGeneratedFilePath &&
            hostDocument.GetProjectContext() is { } projectContext &&
            FindVirtualDocument<TVirtualDocumentSnapshot>(hostDocument.Uri, projectContext) is { } virtualDocument)
        {
            return documentSynchronizer.TryReturnPossiblyFutureSnapshot<TVirtualDocumentSnapshot>(requiredHostDocumentVersion, hostDocument.Uri, virtualDocument.Uri);
        }

        return documentSynchronizer.TryReturnPossiblyFutureSnapshot<TVirtualDocumentSnapshot>(requiredHostDocumentVersion, hostDocument.Uri);
    }

    private CSharpVirtualDocumentSnapshot? FindVirtualDocument<TVirtualDocumentSnapshot>(
        Uri hostDocumentUri,
        VSProjectContext? projectContext) where TVirtualDocumentSnapshot : VirtualDocumentSnapshot
    {
        if (!_documentManager.TryGetDocument(hostDocumentUri, out var documentSnapshot) ||
            !documentSnapshot.TryGetAllVirtualDocuments<TVirtualDocumentSnapshot>(out var virtualDocuments))
        {
            return null;
        }

        foreach (var virtualDocument in virtualDocuments)
        {
            // NOTE: This is _NOT_ the right snapshot, or at least cannot be assumed to be, we just need the Uri
            // to pass to the synchronizer, so it can get the right snapshot
            if (virtualDocument is not CSharpVirtualDocumentSnapshot csharpVirtualDocument)
            {
                Debug.Fail("FindVirtualDocumentUri should only be called for C# documents, as those are the only ones that have multiple virtual documents");
                return null;
            }

            if (IsMatch(csharpVirtualDocument.ProjectKey, projectContext))
            {
                return csharpVirtualDocument;
            }
        }

        return null;

        static bool IsMatch(ProjectKey projectKey, VSProjectContext? projectContext)
        {
            // If we don't have a project key on our virtual document, then it means we don't know about project info
            // yet, so there would only be one virtual document, so return true.
            // If the request doesn't have project context, then we can't reason about which project we're asked about
            // so return true.
            // In both cases we'll just return the first virtual document we find.
            return projectKey.Id is null ||
                projectContext is null ||
                FilePathComparer.Instance.Equals(projectKey.Id, projectContext.Id);
        }
    }
}
