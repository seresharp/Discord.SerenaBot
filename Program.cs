using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Remora.Commands.Extensions;
using Remora.Discord.API.Abstractions.Gateway.Commands;
using Remora.Discord.Caching.Abstractions.Services;
using Remora.Discord.Commands.Extensions;
using Remora.Discord.Commands.Services;
using Remora.Discord.Gateway;
using Remora.Discord.Gateway.Extensions;
using Remora.Discord.Hosting.Extensions;
using Remora.Discord.Interactivity.Extensions;
using Remora.Discord.Interactivity.Services;
using Remora.Rest;
using Remora.Rest.Core;
using Remora.Results;
using SerenaBot.CommandErrorHandlers;
using SerenaBot.Commands;
using SerenaBot.Extensions;
using SerenaBot.Interactions;
using SerenaBot.Responders;
using SerenaBot.Util;
using System.Reflection;
using System.Text.Json;

namespace SerenaBot;

public class Program
{
    private static readonly FieldInfo _connectionStatus = typeof(DiscordGatewayClient).GetField("_connectionStatus", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Field {nameof(DiscordGatewayClient)}._connectionStatus not found");

    public static async Task Main(string[] args)
    {
        await GPT2.GenerateMessageFiles();

        while (true)
        {
            using IHost host = CreateHostBuilder(args)
                .UseConsoleLifetime()
                .Build();

            IServiceProvider services = host.Services;
            ILogger<Program> log = services.GetRequiredService<ILogger<Program>>();
            DiscordGatewayClient gateway = services.GetRequiredService<DiscordGatewayClient>();

            Result slashUpdate = await services.GetRequiredService<SlashService>().UpdateSlashCommandsAsync();
            if (!slashUpdate.IsSuccess)
            {
                log.LogWarning("Failed to update global slash commands: {error}", slashUpdate.Error);
                continue;
            }

            bool ctrlC = false;
            Console.CancelKeyPress += (_, _) => ctrlC = true;

            CancellationTokenSource cts = new();
            Task runBot = host.RunAsync(cts.Token);
            long lastConnected = Environment.TickCount64;
            while (true)
            {
                try
                {
                    await runBot.WaitAsync(TimeSpan.FromSeconds(30));
                    if (cts.IsCancellationRequested)
                    {
                        Console.WriteLine("Host.RunAsync cancelled due to excessive bot downtime. Possible issue in Remora.Discord? Attempting bot recreation");
                        break;
                    }

                    if (runBot.IsCompleted)
                    {
                        if (ctrlC) return;

                        Console.WriteLine("Host.RunAsync finished (not ctrl-c), restarting");
                        break;
                    }
                }
                catch (TimeoutException)
                {
                    GatewayConnectionStatus status = (GatewayConnectionStatus)(_connectionStatus.GetValue(gateway) ?? throw new NullReferenceException("_connectionStatus"));
                    if (status == GatewayConnectionStatus.Connected)
                    {
                        lastConnected = Environment.TickCount64;
                    }

                    if (Environment.TickCount64 - lastConnected >= 5 * 60 * 1000)
                    {
                        cts.Cancel();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error in Host.RunAsync: " + e);
                }
            }
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((_, config) => config.AddJsonFile("Resources/appsettings.json", optional: false, reloadOnChange: true))
        .AddDiscordService(services =>
            services.GetRequiredService<IConfiguration>().GetRequiredValue<string>("BOT_TOKEN")
        )
        .ConfigureLogging(builder => builder.AddConsole())
        .ConfigureServices((_, services) => services
            .Configure<DiscordGatewayClientOptions>(g => g.Intents |= GatewayIntents.MessageContents | GatewayIntents.GuildMembers)
            .Configure<HttpClientFactoryOptions>(o => o.HttpClientActions.Add(http => http.Timeout = TimeSpan.FromSeconds(7.5)))
            .AddSingleton<DiscordAPICache>()
            .AddTransient(s => new RestSteamAPI
            (
                s.GetRequiredService<IRestHttpClient>(),
                s.GetRequiredService<ICacheProvider>(),
                s.GetRequiredService<IConfiguration>()
            ))
            .AddDiscordCommands(true, false)
            .AddResponder<GPT2MessageResponder>()
            .AddPreparationErrorEvent<CommandPreparationErrorHandler>()
            .AddPostExecutionEvent<CommandExecutionErrorHandler>()
            .AddInteractivity()
            .AddSingleton(InMemoryDataService<Snowflake, WaifuCommands.WaifuBuilder>.Instance)
            .AddResponder<WaifuCommands.WaifuBuilder.MessageDeletedResponder>()
            .AddCommandTree(Remora.Discord.Interactivity.Constants.InteractionTree)
                .WithCommandGroup<WaifuInteractions>(true)
            .Finish()
            .AddCommandTree()
                .WithCommandGroup<ColorCommands>(true)
                .WithCommandGroup<PronounCommands>(true)
                .WithCommandGroup<OwlbotCommands>(true)
                .WithCommandGroup<EvalCommands>(true)
                .WithCommandGroup<MiscImageCommands>(true)
                .WithCommandGroup<WaifuCommands>(true)
                .WithCommandGroup<MiscTextCommands>(true)
                .WithCommandGroup<UnitConversionCommands>(true)
                .WithCommandGroup<PhotosLibraryCommands>(true)
                .WithCommandGroup<ReminderCommands>(true)
                .WithCommandGroup<SynergismCommands>(true)
                //.WithCommandGroup<MinesweeperCommands>(true)
                //.WithCommandGroup<MarkovChainCommands>(true)
            .Finish()
        );
}
