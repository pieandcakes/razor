﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor;
using Microsoft.AspNetCore.Razor.LanguageServer.Hosting;
using Microsoft.CodeAnalysis.ExternalAccess.Razor.Cohost;
using Microsoft.CodeAnalysis.Razor.Logging;
using Microsoft.CodeAnalysis.Razor.Remote;
using Microsoft.VisualStudio.LanguageServer.ContainedLanguage;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Microsoft.VisualStudio.Razor.LanguageClient;
using Microsoft.VisualStudio.Razor.LanguageClient.Cohost;
using Microsoft.VisualStudio.Razor.LanguageClient.Extensions;
using Microsoft.VisualStudio.Razor.Settings;
using RLSP = Roslyn.LanguageServer.Protocol;

namespace Microsoft.VisualStudio.LanguageServices.Razor.LanguageClient.Cohost;

#pragma warning disable RS0030 // Do not use banned APIs
[Shared]
[CohostEndpoint(Methods.TextDocumentSignatureHelpName)]
[Export(typeof(IDynamicRegistrationProvider))]
[ExportCohostStatelessLspService(typeof(CohostSignatureHelpEndpoint))]
[method: ImportingConstructor]
#pragma warning restore RS0030 // Do not use banned APIs
internal class CohostSignatureHelpEndpoint(
    IRemoteServiceProvider remoteServiceProvider,
    IClientSettingsManager clientSettingsManager,
    IHtmlDocumentSynchronizer htmlDocumentSynchronizer,
    LSPRequestInvoker requestInvoker,
    ILoggerFactory loggerFactory)
    : AbstractRazorCohostDocumentRequestHandler<SignatureHelpParams, SumType<SignatureHelp, RLSP.SignatureHelp>?>, IDynamicRegistrationProvider
{
    private readonly IRemoteServiceProvider _remoteServiceProvider = remoteServiceProvider;
    private readonly IClientSettingsManager _clientSettingsManager = clientSettingsManager;
    private readonly IHtmlDocumentSynchronizer _htmlDocumentSynchronizer = htmlDocumentSynchronizer;
    private readonly LSPRequestInvoker _requestInvoker = requestInvoker;
    private readonly ILogger _logger = loggerFactory.GetOrCreateLogger<CohostFoldingRangeEndpoint>();

    protected override bool MutatesSolutionState => false;

    protected override bool RequiresLSPSolution => true;

    public Registration? GetRegistration(VSInternalClientCapabilities clientCapabilities, DocumentFilter[] filter, RazorCohostRequestContext requestContext)
    {
        if (clientCapabilities.TextDocument?.SignatureHelp?.DynamicRegistration == true)
        {
            return new Registration()
            {
                Method = Methods.TextDocumentSignatureHelpName,
                RegisterOptions = new SignatureHelpRegistrationOptions()
                {
                    DocumentSelector = filter
                }.EnableSignatureHelp()
            };
        }

        return null;
    }

    protected override RazorTextDocumentIdentifier? GetRazorTextDocumentIdentifier(SignatureHelpParams request)
        => new RazorTextDocumentIdentifier(request.TextDocument.Uri, (request.TextDocument as VSTextDocumentIdentifier)?.ProjectContext?.Id);

    // NOTE: The use of SumType here is a little odd, but it allows us to return Roslyn LSP types from the Roslyn call, and VS LSP types from the Html
    //       call. It works because both sets of types are attributed the right way, so the Json ends up looking the same and the client doesn't
    //       care. Ideally eventually we will be able to move all of this to just Roslyn LSP types, but we might have to wait for Web Tools
    protected async override Task<SumType<SignatureHelp, RLSP.SignatureHelp>?> HandleRequestAsync(SignatureHelpParams request, RazorCohostRequestContext context, CancellationToken cancellationToken)
    {
        // Return nothing if "Parameter Information" option is disabled unless signature help is invoked explicitly via command as opposed to typing or content change
        if (request.Context is { TriggerKind: not SignatureHelpTriggerKind.Invoked } &&
            !_clientSettingsManager.GetClientSettings().ClientCompletionSettings.AutoListParams)
        {
            return null;
        }

        var razorDocument = context.TextDocument.AssumeNotNull();

        var data = await _remoteServiceProvider.TryInvokeAsync<IRemoteSignatureHelpService, RLSP.SignatureHelp?>(
            razorDocument.Project.Solution,
            (service, solutionInfo, cancellationToken) => service.GetSignatureHelpAsync(solutionInfo, razorDocument.Id, new RLSP.Position(request.Position.Line, request.Position.Character), cancellationToken),
            cancellationToken).ConfigureAwait(false);

        // If we got a response back, then either Razor or C# wants to do something with this, so we're good to go
        if (data is { } signatureHelp)
        {
            return signatureHelp;
        }

        // If we didn't get anything from Razor or Roslyn, lets ask Html what they want to do
        var htmlDocument = await _htmlDocumentSynchronizer.TryGetSynchronizedHtmlDocumentAsync(razorDocument, cancellationToken).ConfigureAwait(false);
        if (htmlDocument is null)
        {
            return null;
        }

        request.TextDocument = request.TextDocument.WithUri(htmlDocument.Uri);

        var result = await _requestInvoker.ReinvokeRequestOnServerAsync<SignatureHelpParams, SignatureHelp?>(
            htmlDocument.Buffer,
            Methods.TextDocumentSignatureHelpName,
            RazorLSPConstants.HtmlLanguageServerName,
            request,
            cancellationToken).ConfigureAwait(false);

        return result?.Response;
    }
}
