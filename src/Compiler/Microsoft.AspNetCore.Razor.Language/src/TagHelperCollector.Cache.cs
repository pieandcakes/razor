﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.AspNetCore.Razor.Language;

public abstract partial class TagHelperCollector<T>
    where T : ITagHelperDescriptorProvider
{
    private class Cache
    {
        private const int IncludeDocumentation = 1 << 0;
        private const int ExcludeHidden = 1 << 1;

        // The cache needs to be large enough to handle all combinations of options.
        private const int CacheSize = (IncludeDocumentation | ExcludeHidden) + 1;

        private readonly TagHelperDescriptor[]?[] _tagHelpers = new TagHelperDescriptor[CacheSize][];

        public bool TryGet(bool includeDocumentation, bool excludeHidden, [NotNullWhen(true)] out TagHelperDescriptor[]? tagHelpers)
        {
            var index = CalculateIndex(includeDocumentation, excludeHidden);

            tagHelpers = _tagHelpers[index];
            return tagHelpers is not null;
        }

        public void Add(TagHelperDescriptor[] tagHelpers, bool includeDocumentation, bool excludeHidden)
        {
            var index = CalculateIndex(includeDocumentation, excludeHidden);

            Debug.Assert(_tagHelpers[index] is null);

            _tagHelpers[index] = tagHelpers;
        }

        private static int CalculateIndex(bool includeDocumentation, bool excludeHidden)
        {
            var index = 0;

            if (includeDocumentation)
            {
                index |= IncludeDocumentation;
            }

            if (excludeHidden)
            {
                index |= ExcludeHidden;
            }

            return index;
        }
    }
}
