﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;

namespace Microsoft.AspNetCore.Razor.Telemetry;

public interface ITelemetryReporter
{
    IDisposable BeginScope(string name, Severity severity);
    IDisposable BeginScope<T>(string name, Severity severity, T values) where T : IImmutableDictionary<string, object?>;
    void ReportEvent(string name, Severity severity);
    void ReportEvent<T>(string name, Severity severity, ImmutableDictionary<string, T> values);
    void ReportFault(Exception exception, string? message, params object?[] @params);
}
