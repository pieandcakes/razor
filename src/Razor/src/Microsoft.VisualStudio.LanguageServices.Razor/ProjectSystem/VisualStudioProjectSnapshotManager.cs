﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System.ComponentModel.Composition;
using Microsoft.AspNetCore.Razor.ProjectEngineHost;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;

namespace Microsoft.VisualStudio.Razor.ProjectSystem;

[Export(typeof(IProjectSnapshotManager))]
[method: ImportingConstructor]
internal sealed class VisualStudioProjectSnapshotManager(
    IProjectEngineFactoryProvider projectEngineFactoryProvider,
    IErrorReporter errorReporter)
    : ProjectSnapshotManager(projectEngineFactoryProvider, errorReporter)
{
}
