using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Discord.Commands.Conditions;
using Remora.Discord.Commands.Feedback.Services;
using Remora.Discord.Extensions.Embeds;
using Remora.Results;
using SerenaBot.Commands.Util;
using SerenaBot.Extensions;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace SerenaBot.Commands;

public class EvalCommands : BaseCommandGroup
{
    private static readonly CSharpCompilationOptions Options = new(OutputKind.DynamicallyLinkedLibrary, usings: new[]
    {
        "System",
        "System.Collections",
        "System.Collections.Generic",
        "System.Dynamic",
        "System.IO",
        "System.Text",
        "System.Text.Json",
        "System.Text.RegularExpressions",
        "System.Threading.Tasks",
        "System.Linq",
        "System.Reflection",

        "OneOf",

        "Remora.Commands.Attributes",
        "Remora.Commands.DependencyInjection",
        "Remora.Commands.Extensions",
        "Remora.Commands.Groups",
        "Remora.Commands.Results",
        "Remora.Commands.Trees",
        "Remora.Discord.API",
        "Remora.Discord.API.Abstractions.Gateway.Commands",
        "Remora.Discord.API.Abstractions.Gateway.Events",
        "Remora.Discord.API.Abstractions.Objects",
        "Remora.Discord.API.Abstractions.Rest",
        "Remora.Discord.API.Abstractions.Results",
        "Remora.Discord.API.Errors",
        "Remora.Discord.API.Objects",
        "Remora.Discord.Commands.Attributes",
        "Remora.Discord.Commands.Conditions",
        "Remora.Discord.Commands.Contexts",
        "Remora.Discord.Commands.Extensions",
        "Remora.Discord.Commands.Feedback.Messages",
        "Remora.Discord.Commands.Feedback.Services",
        "Remora.Discord.Commands.Services",
        "Remora.Discord.Extensions.Embeds",
        "Remora.Discord.Gateway",
        "Remora.Discord.Gateway.Extensions",
        "Remora.Discord.Gateway.Responders",
        "Remora.Discord.Gateway.Results",
        "Remora.Discord.Hosting.Extensions",
        "Remora.Discord.Interactivity",
        "Remora.Discord.Interactivity.Extensions",
        "Remora.Discord.Interactivity.Services",
        "Remora.Rest.Core",
        "Remora.Rest.Results",
        "Remora.Results",

        "SerenaBot.Extensions"
    });

    private static readonly IReadOnlyList<MetadataReference> References = new List<MetadataReference>
    (
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(asm => !asm.IsDynamic && !string.IsNullOrWhiteSpace(asm.Location))
            .Select(asm => MetadataReference.CreateFromFile(asm.Location))
            .ToList()
    );

    [Command("eval")]
    [RequireOwner]
    [CommandType(ApplicationCommandType.Message)]
    public async Task<IResult> ExecuteCodeAsync(IMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Content))
        {
            return await Feedback.SendContextualWarningAsync("I can only see messages that ping me");
        }

        Result<IUser> getCurrentUser = await UserAPI.GetCurrentUserAsync();
        if (!getCurrentUser.IsSuccess) return getCurrentUser;

        string botPing = getCurrentUser.Entity.BuildMention();
        string code = message.Content.Remove(message.Content.IndexOf(botPing), botPing.Length).Trim();
        if (code.StartsWith('`'))
        {
            code = code.Trim('`');

            int whitespace = code.Select((c, i) => (c, i)).FirstOrDefault(t => char.IsWhiteSpace(t.c), (default, -1)).i;
            if (whitespace >= 0 && whitespace < code.Length - 1 && code[0..whitespace] is "cs" or "csharp")
            {
                code = code[(whitespace + 1)..].Trim();
            }
        }

        code = $"using Console = {typeof(EvalCommands).FullName}.{typeof(FakeConsole).Name};\n" + code;

        CSharpCompilation comp = CSharpCompilation.CreateScriptCompilation
        (
            "why",
            CSharpSyntaxTree.ParseText
            (
                code,
                CSharpParseOptions.Default
                    .WithKind(SourceCodeKind.Script)
                    .WithLanguageVersion(LanguageVersion.Latest)
            ),
            References,
            Options,
            globalsType: typeof(EvalGlobals)
        );

        Embed GetErrorEmbed(IEnumerable<Diagnostic> errors) => new
        (
            Title: "Compilation failed",
            Description: string.Join('\n', errors.Select(err => $"({err.Location.GetLineSpan().StartLinePosition}): [{err.Id} {err.GetMessage()}]")),
            Colour: Feedback.Theme.FaultOrDanger
        );

        if (comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray() is { Length: > 0 } errors)
        {
            return await Feedback.SendContextualEmbedAsync(GetErrorEmbed(errors));
        }

        AssemblyLoadContext ctx = new("garbage", true);
        try
        {
            await using var mem = new MemoryStream();
            EmitResult emit = comp.Emit(mem);
            if (!emit.Success)
            {
                return await Feedback.SendContextualEmbedAsync(GetErrorEmbed(emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            }

            mem.Position = 0;
            Assembly asm = ctx.LoadFromStream(mem);
            if (comp.GetEntryPoint(default) is not IMethodSymbol entrySymbol)
            {
                return await Feedback.SendContextualErrorAsync("Missing entry point");
            }

            Type? entryType = asm.GetType($"{entrySymbol.ContainingNamespace.MetadataName}.{entrySymbol.ContainingType.MetadataName}");
            MethodInfo? entryMethod = entryType?.GetMethod(entrySymbol.MetadataName);
            if (entryMethod == null)
            {
                return await Feedback.SendContextualErrorAsync($"Couldn't find entry point! Type: {entryType?.Name ?? "null"} Method: {entryMethod?.Name ?? "null"}");
            }

            var del = entryMethod.CreateDelegate<Func<object?[], Task<object?>>>();
            object? res;
            Stopwatch watch;
            try
            {
                FakeConsole.Reset();
                watch = Stopwatch.StartNew();
                res = await del(new object?[] { new EvalGlobals(Config, ChannelAPI, GuildAPI, UserAPI, Logger, Feedback, Http, Random, Context), null });
                watch.Stop();
            }
            catch (Exception e)
            {
                return await Feedback.SendContextualErrorAsync(e.ToString());
            }

            EmbedBuilder embed = new() { Title = "Evaluation Success" };
            embed.AddField("Benchmark",
                watch.Elapsed switch
                {
                    { TotalNanoseconds: < 1000 } => $"{watch.Elapsed.TotalNanoseconds}ns",
                    { Microseconds: < 1000 } => $"{watch.Elapsed.Microseconds}μs",
                    { Milliseconds: < 1000 } => $"{watch.Elapsed.Milliseconds}ms",
                    _ => $"{watch.Elapsed.Seconds}ns"
                }
            );

            string console = FakeConsole.GetOutput();
            if (!string.IsNullOrWhiteSpace(console))
            {
                embed.AddField("Console Output", console);
            }

            if (res != null)
            {
                embed.AddField("Result", $"[{res.GetType().Name}] " + res.ToString());
            }

            Result<Embed> buildEmbed = embed.Build();
            return buildEmbed.IsSuccess
                ? await Feedback.SendContextualEmbedAsync(buildEmbed.Entity)
                : buildEmbed;
        }
        finally
        {
            ctx.Unload();
        }
    }

    public record EvalGlobals(IConfiguration Config, IDiscordRestChannelAPI ChannelAPI, IDiscordRestGuildAPI GuildAPI,
        IDiscordRestUserAPI UserAPI, ILogger Logger, FeedbackService Feedback, HttpClient Http, Random Random, IContextHelper Context);

    public static class FakeConsole
    {
        private readonly static StringBuilder Output = new();

        public static void Write(object value)
            => Output.Append(value?.ToString() ?? string.Empty);

        public static void WriteLine(object value)
            => Output.AppendLine(value?.ToString() ?? string.Empty);

        public static void Reset()
            => Output.Clear();

        public static string GetOutput()
            => Output.ToString();
    }
}
