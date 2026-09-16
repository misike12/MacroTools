using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using Microsoft.Extensions.DependencyInjection;
using R6Md;
using R6Md.Config;
using R6Md.Replays;

// Identity, description and icon are not set here: they come from manifest.json at the content root.
// Strings is generated from Localization/*.resx, so UseLocalization is what makes every LocalizedString
// below resolve in the user's language rather than falling back to its key.
var builder = MacroDeckPlugin.CreatePlugin(args);
builder.Services.AddSingleton<R6SettingsProvider>();
builder.Services.AddSingleton<ReplayService>();
builder.Services.AddSingleton<OverwolfBridge>();
var plugin = builder
	.UseMacroDeckLogging()
	.UseLocalization(Strings.LocalizationCatalog)
	.RegisterIntegration<PluginIntegration>()
	.Build();

await plugin.RunAsync();
