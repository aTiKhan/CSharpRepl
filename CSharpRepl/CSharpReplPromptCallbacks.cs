﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.SymbolExploration;
using CSharpRepl.Services.SyntaxHighlighting;
using PrettyPrompt;
using PrettyPrompt.Completion;
using PrettyPrompt.Consoles;
using PrettyPrompt.Documents;
using PrettyPrompt.Highlighting;

namespace CSharpRepl.PrettyPromptConfig;

internal class CSharpReplPromptCallbacks : PromptCallbacks
{
    private readonly IConsole console;
    private readonly RoslynServices roslyn;

    public CSharpReplPromptCallbacks(IConsole console, RoslynServices roslyn)
    {
        this.console = console;
        this.roslyn = roslyn;
    }

    protected override IEnumerable<(KeyPressPattern Pattern, KeyPressCallbackAsync Callback)> GetKeyPressCallbacks()
    {
        yield return (
            new(ConsoleKey.F1),
            async (text, caret, cancellationToken) => LaunchDocumentation(await roslyn.GetSymbolAtIndexAsync(text, caret)));

        yield return (
            new(ConsoleModifiers.Control, ConsoleKey.F1),
            async (text, caret, cancellationToken) => LaunchSource(await roslyn.GetSymbolAtIndexAsync(text, caret)));

        yield return (
            new(ConsoleKey.F9),
            (text, caret, cancellationToken) => Disassemble(roslyn, text, console, debugMode: true));

        yield return (
            new(ConsoleModifiers.Control, ConsoleKey.F9),
            (text, caret, cancellationToken) => Disassemble(roslyn, text, console, debugMode: false));

        yield return (
            new(ConsoleKey.F12),
            async (text, caret, cancellationToken) => LaunchSource(await roslyn.GetSymbolAtIndexAsync(text, caret)));

        yield return (
            new(ConsoleModifiers.Control, ConsoleKey.D),
            (text, caret, cancellationToken) => Task.FromResult<KeyPressCallbackResult?>(new ExitApplicationKeyPress()));
    }

    protected override async Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(string text, int caret, TextSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        var completions = await roslyn.CompleteAsync(text, caret).ConfigureAwait(false);
        return completions
              .OrderByDescending(i => i.Item.Rules.MatchPriority)
              .ThenBy(i => i.Item.SortText)
              .Select(r => new CompletionItem(
                  replacementText: r.Item.DisplayText,
                  displayText: r.DisplayText,
                  getExtendedDescription: r.GetDescriptionAsync,
                  filterText: r.Item.FilterText
              ))
              .ToArray();
    }

    protected override async Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(string text, CancellationToken cancellationToken)
    {
        var classifications = await roslyn.SyntaxHighlightAsync(text).ConfigureAwait(false);
        return classifications.ToFormatSpans();
    }

    protected override async Task<bool> InterpretKeyPressAsInputSubmitAsync(string text, int caret, ConsoleKeyInfo keyInfo, CancellationToken cancellationToken) =>
        keyInfo.Modifiers == default && 
        keyInfo.Key == ConsoleKey.Enter && 
        await roslyn.IsTextCompleteStatementAsync(text).ConfigureAwait(false);

    private static async Task<KeyPressCallbackResult?> Disassemble(RoslynServices roslyn, string text, IConsole console, bool debugMode)
    {
        var result = await roslyn.ConvertToIntermediateLanguage(text, debugMode);

        switch (result)
        {
            case EvaluationResult.Success success:
                var ilCode = success.ReturnValue.ToString()!;
                var highlighting = await roslyn.SyntaxHighlightAsync(ilCode).ConfigureAwait(false);
                var syntaxHighlightedOutput = Prompt.RenderAnsiOutput(ilCode, highlighting.ToFormatSpans(), console.BufferWidth);
                return new KeyPressCallbackResult(text, syntaxHighlightedOutput);
            case EvaluationResult.Error err:
                return new KeyPressCallbackResult(text, AnsiColor.Red.GetEscapeSequence() + err.Exception.Message + AnsiEscapeCodes.Reset);
            default:
                // this should never happen, as the disassembler cannot be cancelled.
                throw new InvalidOperationException("Could not process disassembly result");
        }
    }

    private static KeyPressCallbackResult? LaunchDocumentation(SymbolResult type)
    {
        if (type != SymbolResult.Unknown && type.SymbolDisplay is not null)
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture.Name;
            LaunchBrowser($"https://docs.microsoft.com/{culture}/dotnet/api/{type.SymbolDisplay}");
        }
        return null;
    }

    private static KeyPressCallbackResult? LaunchSource(SymbolResult type)
    {
        if (type.Url is not null)
        {
            LaunchBrowser(type.Url);
        }
        else if (type != SymbolResult.Unknown && type.SymbolDisplay is not null)
        {
            LaunchBrowser($"https://source.dot.net/#q={type.SymbolDisplay}");
        }

        return null;
    }

    private static KeyPressCallbackResult? LaunchBrowser(string url)
    {
        var opener =
            OperatingSystem.IsWindows() ? "explorer" :
            OperatingSystem.IsMacOS() ? "open" :
            "xdg-open";

        var browser = Process.Start(new ProcessStartInfo(opener, '"' + url + '"')); // wrap in quotes so we can pass through url hashes (#)
        browser?.WaitForExit(); // wait for exit seems to make this work better on WSL2.

        return null;
    }
}

/// <summary>
/// Used when the user presses an "exit application" key combo (ctrl-d) to instruct the main REPL loop to end.
/// </summary>
sealed class ExitApplicationKeyPress : KeyPressCallbackResult
{
    public ExitApplicationKeyPress()
        : base(string.Empty, null)
    { }
}
