﻿using System;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.Razor.Workspaces;

internal static class IRazorGeneratedDocumentExtensions
{
    public static SourceText GetGeneratedSourceText(this IRazorGeneratedDocument generatedDocument)
    {
        if (generatedDocument.CodeDocument is not { } codeDocument)
        {
            throw new InvalidOperationException("Cannot use document mapping service on a generated document that has a null CodeDocument.");
        }

        return codeDocument.GetGeneratedSourceText(generatedDocument);
    }
}
