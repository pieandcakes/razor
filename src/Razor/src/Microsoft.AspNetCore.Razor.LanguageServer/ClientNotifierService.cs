﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace Microsoft.AspNetCore.Razor.LanguageServer;

// We need to wait until everything is initialized before we make any client requests, so
// this class takes a TCS which will complete when everything is initialized.
internal sealed class ClientNotifierService(JsonRpc jsonRpc) : IClientNotifierService, IOnInitialized
{
    private readonly JsonRpc _jsonRpc = jsonRpc ?? throw new ArgumentNullException(nameof(jsonRpc));
    private readonly TaskCompletionSource<bool> _initializedCompletionSource = new TaskCompletionSource<bool>();

    public async Task<TResponse> SendRequestAsync<TParams, TResponse>(string method, TParams @params, CancellationToken cancellationToken)
    {
        await _initializedCompletionSource.Task.ConfigureAwait(false);

        return await _jsonRpc.InvokeWithParameterObjectAsync<TResponse>(method, @params, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendNotificationAsync<TParams>(string method, TParams @params, CancellationToken cancellationToken)
    {
        await _initializedCompletionSource.Task.ConfigureAwait(false);

        await _jsonRpc.NotifyWithParameterObjectAsync(method, @params).ConfigureAwait(false);
    }

    public async Task SendNotificationAsync(string method, CancellationToken cancellationToken)
    {
        await _initializedCompletionSource.Task.ConfigureAwait(false);

        await _jsonRpc.NotifyAsync(method).ConfigureAwait(false);
    }

    /// <summary>
    /// Fires when the language server is set to "Started".
    /// </summary>
    public Task OnInitializedAsync(VSInternalClientCapabilities clientCapabilities, CancellationToken cancellationToken)
    {
        _initializedCompletionSource.TrySetResult(true);
        return Task.CompletedTask;
    }
}
