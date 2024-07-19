﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Razor;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis.Razor.Logging;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using RLSP = Roslyn.LanguageServer.Protocol;

namespace Microsoft.CodeAnalysis.Razor.Workspaces;

internal static class PositionExtensions
{
    public static LinePosition ToLinePosition(this Position position)
        => new LinePosition(position.Line, position.Character);

    public static LinePosition ToLinePosition(this RLSP.Position position)
        => new LinePosition(position.Line, position.Character);
    public static bool TryGetAbsoluteIndex(this Position position, SourceText text, ILogger? logger, out int absoluteIndex)
    {
        ArgHelper.ThrowIfNull(position);
        ArgHelper.ThrowIfNull(text);

        return text.TryGetAbsoluteIndex(position.Line, position.Character, logger, out absoluteIndex);
    }

    public static bool TryGetSourceLocation(
        this Position position,
        SourceText text,
        ILogger? logger,
        [NotNullWhen(true)] out SourceLocation? sourceLocation)
    {
        if (!position.TryGetAbsoluteIndex(text, logger, out var absoluteIndex))
        {
            sourceLocation = null;
            return false;
        }

        sourceLocation = new SourceLocation(absoluteIndex, position.Line, position.Character);
        return true;
    }

    public static int GetRequiredAbsoluteIndex(this Position position, SourceText text, ILogger? logger)
    {
        if (!position.TryGetAbsoluteIndex(text, logger, out var absoluteIndex))
        {
            throw new InvalidOperationException();
        }

        return absoluteIndex;
    }

    public static int CompareTo(this Position position, Position other)
    {
        ArgHelper.ThrowIfNull(position);
        ArgHelper.ThrowIfNull(other);

        var result = position.Line.CompareTo(other.Line);
        return result != 0 ? result : position.Character.CompareTo(other.Character);
    }

    public static bool IsValid(this Position position, SourceText text)
    {
        ArgHelper.ThrowIfNull(position);
        ArgHelper.ThrowIfNull(text);

        return text.TryGetAbsoluteIndex(position.Line, position.Character, logger: null, out _);
    }
}
