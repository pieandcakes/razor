﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor;
using Microsoft.AspNetCore.Razor.PooledObjects;
using Microsoft.AspNetCore.Razor.Telemetry;
using Microsoft.AspNetCore.Razor.Test.Common;
using Microsoft.CodeAnalysis.ExternalAccess.Razor;
using Microsoft.CodeAnalysis.Razor.CodeActions.Models;
using Microsoft.CodeAnalysis.Razor.Protocol.CodeActions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Microsoft.VisualStudio.Razor.Settings;
using Roslyn.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using WorkspacesSR = Microsoft.CodeAnalysis.Razor.Workspaces.Resources.SR;

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

public class CohostCodeActionsEndpointTest(ITestOutputHelper testOutputHelper) : CohostEndpointTestBase(testOutputHelper)
{
    [Fact]
    public async Task GenerateConstructor()
    {
        var input = """

            <div></div>

            @code
            {
                public class [||]Goo
                {
                }
            }

            """;

        var expected = """
            
            <div></div>
            
            @code
            {
                public class Goo
                {
                    public Goo()
                    {
                    }
                }
            }

            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.GenerateDefaultConstructors);
    }

    [Fact]
    public async Task UseExpressionBodiedMember()
    {
        var input = """
            @using System.Linq

            <div></div>

            @code
            {
                [|{|selection:|}void M(string[] args)|]
                {
                    args.ToString();
                }
            }

            """;

        var expected = """
            @using System.Linq

            <div></div>
            
            @code
            {
                void M(string[] args) => args.ToString();
            }

            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.UseExpressionBody);
    }

    [Fact]
    public async Task IntroduceLocal()
    {
        var input = """
            @using System.Linq

            <div></div>

            @code
            {
                void M(string[] args)
                {
                    if ([|args.First()|].Length > 0)
                    {
                    }
                    if (args.First().Length > 0)
                    {
                    }
                }
            }

            """;

        var expected = """
            @using System.Linq

            <div></div>
            
            @code
            {
                void M(string[] args)
                {
                    string v = args.First();
                    if (v.Length > 0)
                    {
                    }
                    if (args.First().Length > 0)
                    {
                    }
                }
            }

            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.IntroduceVariable);
    }

    [Fact]
    public async Task IntroduceLocal_All()
    {
        var input = """
            @using System.Linq

            <div></div>

            @code
            {
                void M(string[] args)
                {
                    if ([|args.First()|].Length > 0)
                    {
                    }
                    if (args.First().Length > 0)
                    {
                    }
                }
            }

            """;

        var expected = """
            @using System.Linq

            <div></div>
            
            @code
            {
                void M(string[] args)
                {
                    string v = args.First();
                    if (v.Length > 0)
                    {
                    }
                    if (v.Length > 0)
                    {
                    }
                }
            }

            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.IntroduceVariable, childActionIndex: 1);
    }

    [Fact]
    public async Task ConvertConcatenationToInterpolatedString_CSharpStatement()
    {
        var input = """
            @{
                var x = "he[||]l" + "lo" + Environment.NewLine + "world";
            }
            """;

        var expected = """
            @{
                var x = $"hello{Environment.NewLine}world";
            }
            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.ConvertConcatenationToInterpolatedString);
    }

    [Fact]
    public async Task ConvertConcatenationToInterpolatedString_ExplicitExpression()
    {
        var input = """
            @("he[||]l" + "lo" + Environment.NewLine + "world")
            """;

        var expected = """
            @($"hello{Environment.NewLine}world")
            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.ConvertConcatenationToInterpolatedString);
    }

    [Fact]
    public async Task ConvertConcatenationToInterpolatedString_CodeBlock()
    {
        var input = """
            @code
            {
                private string _x = "he[||]l" + "lo" + Environment.NewLine + "world";
            }
            """;

        var expected = """
            @code
            {
                private string _x = $"hello{Environment.NewLine}world";
            }
            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.ConvertConcatenationToInterpolatedString);
    }

    [Fact]
    public async Task ConvertBetweenRegularAndVerbatimInterpolatedString_CodeBlock()
    {
        var input = """
            @code
            {
                private string _x = $@"h[||]ello world";
            }
            """;

        var expected = """
            @code
            {
                private string _x = $"hello world";
            }
            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.ConvertBetweenRegularAndVerbatimInterpolatedString);
    }

    [Fact]
    public async Task ConvertBetweenRegularAndVerbatimInterpolatedString_CodeBlock2()
    {
        var input = """
            @code
            {
                private string _x = $"h[||]ello\\nworld";
            }
            """;

        var expected = """
            @code
            {
                private string _x = $@"hello\nworld";
            }
            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.ConvertBetweenRegularAndVerbatimInterpolatedString);
    }

    [Fact]
    public async Task ConvertBetweenRegularAndVerbatimString_CodeBlock()
    {
        var input = """
            @code
            {
                private string _x = @"h[||]ello world";
            }
            """;

        var expected = """
            @code
            {
                private string _x = "hello world";
            }
            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.ConvertBetweenRegularAndVerbatimString);
    }

    [Fact]
    public async Task ConvertBetweenRegularAndVerbatimString_CodeBlock2()
    {
        var input = """
            @code
            {
                private string _x = "h[||]ello\\nworld";
            }
            """;

        var expected = """
            @code
            {
                private string _x = @"hello\nworld";
            }
            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.ConvertBetweenRegularAndVerbatimString);
    }

    [Fact]
    public async Task ConvertPlaceholderToInterpolatedString_CodeBlock()
    {
        var input = """
            @code
            {
                private string _x = [|string.Format("hello{0}world", Environment.NewLine)|];
            }
            """;

        var expected = """
            @code
            {
                private string _x = $"hello{Environment.NewLine}world";
            }
            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.ConvertPlaceholderToInterpolatedString);
    }

    [Fact]
    public async Task ConvertToInterpolatedString_CodeBlock()
    {
        var input = """
            @code
            {
                private string _x = [||]"hello {";
            }
            """;

        var expected = """
            @code
            {
                private string _x = $"hello {{";
            }
            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.ConvertToInterpolatedString);
    }

    [Fact]
    public async Task AddDebuggerDisplay()
    {
        var input = """
            @code {
                class Goo[||]
                {
                    
                }
            }
            """;

        var expected = """
            @using System.Diagnostics
            @code {
                [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
                class Goo
                {
                    private string GetDebuggerDisplay()
                    {
                        return ToString();
                    }
                }
            }
            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeRefactoringProviderNames.AddDebuggerDisplay);
    }

    [Fact]
    public async Task AddUsing()
    {
        var input = """
            @code
            {
                private [||]StringBuilder _x = new StringBuilder();
            }
            """;

        var expected = """
            @using System.Text
            @code
            {
                private StringBuilder _x = new StringBuilder();
            }
            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeFixProviderNames.AddImport);
    }

    [Fact]
    public async Task AddUsing_WithExisting()
    {
        var input = """
            @using System
            @using System.Collections.Generic

            @code
            {
                private [||]StringBuilder _x = new StringBuilder();
            }
            """;

        var expected = """
            @using System
            @using System.Collections.Generic
            @using System.Text

            @code
            {
                private StringBuilder _x = new StringBuilder();
            }
            """;

        await VerifyCodeActionAsync(input, expected, RazorPredefinedCodeFixProviderNames.AddImport);
    }

    [Fact]
    public async Task GenerateEventHandler_NoCodeBlock()
    {
        var input = """
            <button @onclick="{|CS0103:Does[||]NotExist|}"></button>
            """;

        var expected = """
            <button @onclick="DoesNotExist"></button>
            @code {
                private void DoesNotExist(MouseEventArgs e)
                {
                    throw new NotImplementedException();
                }
            }
            """;

        await VerifyCodeActionAsync(input, expected, WorkspacesSR.FormatGenerate_Event_Handler_Title("DoesNotExist"));
    }

    [Fact]
    public async Task GenerateEventHandler_CodeBlock()
    {
        var input = """
            <button @onclick="{|CS0103:Does[||]NotExist|}"></button>

            @code
            {
            }
            """;

        var expected = """
            <button @onclick="DoesNotExist"></button>

            @code
            {
                private void DoesNotExist(MouseEventArgs e)
                {
                    throw new NotImplementedException();
                }
            }
            """;

        await VerifyCodeActionAsync(input, expected, WorkspacesSR.FormatGenerate_Event_Handler_Title("DoesNotExist"));
    }

    [Fact]
    public async Task GenerateAsyncEventHandler_NoCodeBlock()
    {
        var input = """
            <button @onclick="{|CS0103:Does[||]NotExist|}"></button>
            """;

        var expected = """
            <button @onclick="DoesNotExist"></button>
            @code {
                private Task DoesNotExist(MouseEventArgs e)
                {
                    throw new NotImplementedException();
                }
            }
            """;

        await VerifyCodeActionAsync(input, expected, WorkspacesSR.FormatGenerate_Async_Event_Handler_Title("DoesNotExist"));
    }

    [Fact]
    public async Task GenerateAsyncEventHandler_CodeBlock()
    {
        var input = """
            <button @onclick="{|CS0103:Does[||]NotExist|}"></button>

            @code
            {
            }
            """;

        var expected = """
            <button @onclick="DoesNotExist"></button>

            @code
            {
                private Task DoesNotExist(MouseEventArgs e)
                {
                    throw new NotImplementedException();
                }
            }
            """;

        await VerifyCodeActionAsync(input, expected, WorkspacesSR.FormatGenerate_Async_Event_Handler_Title("DoesNotExist"));
    }

    [Fact]
    public async Task ExtractToCodeBehind()
    {
        await VerifyCodeActionAsync(
            input: """
                <div></div>

                @co[||]de
                {
                    private int x = 1;
                }
                """,
            expected: """
                <div></div>


                """,
            codeActionName: WorkspacesSR.ExtractTo_CodeBehind_Title,
            additionalExpectedFiles: [
                (FileUri("File1.razor.cs"), """
                    namespace SomeProject
                    {
                        public partial class File1
                        {
                            private int x = 1;
                        }
                    }
                    """)]);
    }

    [Fact]
    public async Task ExtractToComponent()
    {
        await VerifyCodeActionAsync(
            input: """
                <div></div>

                [|<div>
                    Hello World
                </div>|]

                <div></div>
                """,
            expected: """
                <div></div>

                <Component />

                <div></div>
                """,
            codeActionName: WorkspacesSR.ExtractTo_Component_Title,
            additionalExpectedFiles: [
                (FileUri("Component.razor"), """
                    <div>
                        Hello World
                    </div>
                    """)]);
    }

    private async Task VerifyCodeActionAsync(TestCode input, string? expected, string codeActionName, int childActionIndex = 0, string? fileKind = null, (Uri fileUri, string contents)[]? additionalExpectedFiles = null)
    {
        UpdateClientLSPInitializationOptions(options =>
        {
            options.ClientCapabilities.TextDocument = new()
            {
                CodeAction = new()
                {
                    ResolveSupport = new()
                }
            };

            return options;
        });

        var document = await CreateProjectAndRazorDocumentAsync(input.Text, fileKind, createSeparateRemoteAndLocalWorkspaces: true);

        var codeAction = await VerifyCodeActionRequestAsync(document, input, codeActionName, childActionIndex);

        if (codeAction is null)
        {
            Assert.Null(expected);
            return;
        }

        await VerifyCodeActionResolveAsync(document, codeAction, expected, additionalExpectedFiles);
    }

    private async Task<CodeAction?> VerifyCodeActionRequestAsync(CodeAnalysis.TextDocument document, TestCode input, string codeActionName, int childActionIndex)
    {
        var requestInvoker = new TestLSPRequestInvoker();
        var endpoint = new CohostCodeActionsEndpoint(RemoteServiceInvoker, ClientCapabilitiesService, TestHtmlDocumentSynchronizer.Instance, requestInvoker, NoOpTelemetryReporter.Instance);
        var inputText = await document.GetTextAsync(DisposalToken);

        using var diagnostics = new PooledArrayBuilder<Diagnostic>();
        foreach (var (code, spans) in input.NamedSpans)
        {
            if (code.Length == 0)
            {
                continue;
            }

            foreach (var diagnosticSpan in spans)
            {
                diagnostics.Add(new Diagnostic
                {
                    Code = code,
                    Range = inputText.GetRange(diagnosticSpan)
                });
            }
        }

        var request = new VSCodeActionParams
        {
            TextDocument = new VSTextDocumentIdentifier { Uri = document.CreateUri() },
            Range = inputText.GetRange(input.Span),
            Context = new VSInternalCodeActionContext() { Diagnostics = diagnostics.ToArray() }
        };

        if (input.TryGetNamedSpans("selection", out var selectionSpans))
        {
            // Simulate VS range vs selection range
            request.Context.SelectionRange = inputText.GetRange(selectionSpans.Single());
        }

        var result = await endpoint.GetTestAccessor().HandleRequestAsync(document, request, DisposalToken);

        if (result is null)
        {
            return null;
        }

        Assert.NotNull(result);
        Assert.NotEmpty(result);

        var codeActionToRun = (VSInternalCodeAction?)result.SingleOrDefault(e => ((RazorVSInternalCodeAction)e.Value!).Name == codeActionName || ((RazorVSInternalCodeAction)e.Value!).Title == codeActionName).Value;
        Assert.NotNull(codeActionToRun);

        if (codeActionToRun.Children?.Length > 0)
        {
            codeActionToRun = codeActionToRun.Children[childActionIndex];
        }

        Assert.NotNull(codeActionToRun);
        return codeActionToRun;
    }

    private async Task VerifyCodeActionResolveAsync(CodeAnalysis.TextDocument document, CodeAction codeAction, string? expected, (Uri fileUri, string contents)[]? additionalExpectedFiles = null)
    {
        var requestInvoker = new TestLSPRequestInvoker();
        var clientSettingsManager = new ClientSettingsManager(changeTriggers: []);

        var endpoint = new CohostCodeActionsResolveEndpoint(RemoteServiceInvoker, ClientCapabilitiesService, clientSettingsManager, TestHtmlDocumentSynchronizer.Instance, requestInvoker);

        var result = await endpoint.GetTestAccessor().HandleRequestAsync(document, codeAction, DisposalToken);

        Assert.NotNull(result?.Edit);

        var workspaceEdit = result.Edit;
        Assert.True(workspaceEdit.TryGetTextDocumentEdits(out var documentEdits));

        var documentUri = document.CreateUri();
        var sourceText = await document.GetTextAsync(DisposalToken).ConfigureAwait(false);

        foreach (var edit in documentEdits)
        {
            if (edit.TextDocument.Uri == documentUri)
            {
                sourceText = sourceText.WithChanges(edit.Edits.Select(sourceText.GetTextChange));
            }
            else
            {
                var contents = Assert.Single(additionalExpectedFiles.AssumeNotNull(), f => f.fileUri == edit.TextDocument.Uri).contents;
                AssertEx.EqualOrDiff(contents, Assert.Single(edit.Edits).NewText);
            }
        }

        AssertEx.EqualOrDiff(expected, sourceText.ToString());
    }
}
