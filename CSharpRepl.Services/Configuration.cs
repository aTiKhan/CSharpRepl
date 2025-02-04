﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Theming;
using PrettyPrompt;
using PrettyPrompt.Consoles;

namespace CSharpRepl.Services;

/// <summary>
/// Configuration from command line parameters
/// </summary>
public sealed class Configuration
{
    public const string FrameworkDefault = SharedFramework.NetCoreApp;

    public static readonly string ApplicationDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".csharprepl");

    public static readonly IReadOnlyCollection<string> SymbolServers = new[]
    {
        "https://symbols.nuget.org/download/symbols/",
        "http://msdl.microsoft.com/download/symbols/"
    };

    public HashSet<string> References { get; }
    public HashSet<string> Usings { get; }
    public string Framework { get; }
    public bool Trace { get; }
    public Theme Theme { get; }
    public string? LoadScript { get; }
    public string[] LoadScriptArgs { get; }
    public string? OutputForEarlyExit { get; }
    public KeyBindings KeyBindings { get; }

    public Configuration(
        string[]? references = null,
        string[]? usings = null,
        string? framework = null,
        bool trace = false,
        string? theme = null,
        string? loadScript = null,
        string[]? loadScriptArgs = null,
        string? outputForEarlyExit = null,
        string[]? commitCompletionKeyPatterns = null,
        string[]? triggerCompletionListKeyPatterns = null,
        string[]? newLineKeyPatterns = null,
        string[]? submitPromptKeyPatterns = null)
    {
        References = references?.ToHashSet() ?? new HashSet<string>();
        Usings = usings?.ToHashSet() ?? new HashSet<string>();
        Framework = framework ?? FrameworkDefault;
        Trace = trace;

        Theme =
            string.IsNullOrEmpty(theme) ?
            Theme.DefaultTheme :
             JsonSerializer.Deserialize<Theme>(
                File.ReadAllText(theme),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
              ) ?? Theme.DefaultTheme;

        LoadScript = loadScript;
        LoadScriptArgs = loadScriptArgs ?? Array.Empty<string>();
        OutputForEarlyExit = outputForEarlyExit;

        var commitCompletion =
            commitCompletionKeyPatterns?.Any() == true
            ? ParseKeyPressPatterns(commitCompletionKeyPatterns)
            : new KeyPressPatterns(new(ConsoleKey.Enter), new(ConsoleKey.Tab), new(' '), new('.'), new('('));

        var triggerCompletionList =
            triggerCompletionListKeyPatterns?.Any() == true
            ? ParseKeyPressPatterns(triggerCompletionListKeyPatterns)
            : new KeyPressPatterns(new(ConsoleModifiers.Control, ConsoleKey.Spacebar), new(ConsoleModifiers.Control, ConsoleKey.J));

        var newLine = newLineKeyPatterns?.Any() == true ? ParseKeyPressPatterns(newLineKeyPatterns) : default;
        var submitPrompt = submitPromptKeyPatterns?.Any() == true ? ParseKeyPressPatterns(submitPromptKeyPatterns) : default;

        KeyBindings = new(commitCompletion, triggerCompletionList, newLine, submitPrompt);
    }

    private static KeyPressPatterns ParseKeyPressPatterns(string[] keyPatterns)
        => keyPatterns.Select(ParseKeyPressPattern).ToArray();

    internal static KeyPressPattern ParseKeyPressPattern(string keyPattern)
    {
        if (string.IsNullOrEmpty(keyPattern)) return default;

        const string GeneralInfo = "Key pattern must contain one key with optional modifiers (Alt/Shift/Control). E.g. 'Enter', 'Control+A', '(', 'Alt+.', ...";

        ConsoleKey? key = null;
        char? keyChar = null;
        ConsoleModifiers modifiers = default;
        foreach (var part in keyPattern.Split('+'))
        {
            if (Enum.TryParse<ConsoleKey>(part, ignoreCase: true, out var parsedKey))
            {
                if (key != null) Throw();
                key = parsedKey;
            }
            else if (TryParseConsoleModifiers(part, out var parsedModifier))
            {
                modifiers |= parsedModifier;
            }
            else if (part.Length == 1)
            {
                if (keyChar != null) Throw();
                keyChar = part[0];
            }
            else
            {
                throw new ArgumentException($"Unable to parse '{part}'. {GeneralInfo}", nameof(keyPattern));
            }
        }

        if (!(key.HasValue ^ keyChar.HasValue)) Throw();

        if (key.HasValue)
        {
            return new KeyPressPattern(modifiers, key.Value);
        }
        else
        {
            Debug.Assert(keyChar != null);
            if (modifiers != default) throw new ArgumentException($"Key patterns currently does not support '{keyChar.Value}' with modifiers.", nameof(keyPattern));
            return new KeyPressPattern(keyChar.Value);
        }

        static void Throw() => throw new ArgumentException(GeneralInfo, nameof(keyPattern));

        static bool TryParseConsoleModifiers(string text, out ConsoleModifiers result)
        {
            if (Enum.TryParse(text, ignoreCase: true, out result))
            {
                return true;
            }
            else if (text.Equals("ctrl", StringComparison.OrdinalIgnoreCase))
            {
                result = ConsoleModifiers.Control;
                return true;
            }
            result = default;
            return false;
        }
    }
}