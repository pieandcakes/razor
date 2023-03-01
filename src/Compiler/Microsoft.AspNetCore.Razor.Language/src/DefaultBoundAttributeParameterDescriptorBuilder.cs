﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Collections.Generic;
using Microsoft.AspNetCore.Razor.PooledObjects;

namespace Microsoft.AspNetCore.Razor.Language;

internal class DefaultBoundAttributeParameterDescriptorBuilder : BoundAttributeParameterDescriptorBuilder
{
    private readonly DefaultBoundAttributeDescriptorBuilder _parent;
    private readonly string _kind;
    private readonly Dictionary<string, string> _metadata;

    private RazorDiagnosticCollection _diagnostics;

    public DefaultBoundAttributeParameterDescriptorBuilder(DefaultBoundAttributeDescriptorBuilder parent, string kind)
    {
        _parent = parent;
        _kind = kind;

        _metadata = new Dictionary<string, string>();
    }

    public override string Name { get; set; }

    public override string TypeName { get; set; }

    public override bool IsEnum { get; set; }

    public override string Documentation { get; set; }

    public override string DisplayName { get; set; }

    public override IDictionary<string, string> Metadata => _metadata;

    public override RazorDiagnosticCollection Diagnostics => _diagnostics ??= new RazorDiagnosticCollection();

    internal bool CaseSensitive => _parent.CaseSensitive;

    public BoundAttributeParameterDescriptor Build()
    {
        using var _ = HashSetPool<RazorDiagnostic>.GetPooledObject(out var diagnostics);

        Validate(diagnostics);

        if (_diagnostics is { } existingDiagnostics)
        {
            diagnostics.UnionWith(existingDiagnostics);
        }

        var descriptor = new DefaultBoundAttributeParameterDescriptor(
            _kind,
            Name,
            TypeName,
            IsEnum,
            Documentation,
            GetDisplayName(),
            CaseSensitive,
            new Dictionary<string, string>(Metadata),
            diagnostics.ToArray());

        return descriptor;
    }

    private string GetDisplayName()
    {
        if (DisplayName != null)
        {
            return DisplayName;
        }

        return $":{Name}";
    }

    private void Validate(HashSet<RazorDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            var diagnostic = RazorDiagnosticFactory.CreateTagHelper_InvalidBoundAttributeParameterNullOrWhitespace(_parent.Name);

            diagnostics.Add(diagnostic);
        }
        else
        {
            foreach (var character in Name)
            {
                if (char.IsWhiteSpace(character) || HtmlConventions.IsInvalidNonWhitespaceHtmlCharacters(character))
                {
                    var diagnostic = RazorDiagnosticFactory.CreateTagHelper_InvalidBoundAttributeParameterName(
                        _parent.Name,
                        Name,
                        character);

                    diagnostics.Add(diagnostic);
                }
            }
        }
    }
}
