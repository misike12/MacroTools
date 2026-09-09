using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.References;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Widgets;

public sealed record WidgetOptions(bool ShowAlbum, bool ShowProgress, bool ShowControls, bool Compact)
{
	public static WidgetOptions Default { get; } = new(true, true, true, false);

	public static WidgetOptions FromData(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return Default;
		}

		return new WidgetOptions(
			ShowAlbum: ReadFlag(data, "showAlbum", true),
			ShowProgress: ReadFlag(data, "showProgress", true),
			ShowControls: ReadFlag(data, "showControls", true),
			Compact: ReadFlag(data, "compactMode", false));
	}

	private static bool ReadFlag(JsonElement data, string name, bool fallback) =>
		data.TryGetProperty(name, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
			? element.GetBoolean()
			: fallback;
}

public sealed record WidgetContent(
	string Title,
	string Artist,
	string Album,
	string ProgressText,
	UiProgressReference Progress,
	bool IsPlaying,
	bool HasMedia,
	WidgetOptions Options,
	string Accent,
	string AccentDark)
{
	public static WidgetContent Empty { get; } = new(
		string.Empty, string.Empty, string.Empty, string.Empty,
		new UiProgressReference { PositionMs = 0, Anchor = DateTimeOffset.UtcNow },
		false, false, WidgetOptions.Default, string.Empty, string.Empty);

	public static WidgetContent FromSnapshot(MediaSnapshot snapshot, WidgetOptions options, string accent, string accentDark)
	{
		var durationMs = snapshot.Duration > TimeSpan.Zero ? (long?)snapshot.Duration.TotalMilliseconds : null;
		return new WidgetContent(
			snapshot.Title,
			snapshot.Artist,
			snapshot.Album,
			snapshot.HasSession ? $"{MediaText.FormatDuration(snapshot.Position)} / {MediaText.FormatDuration(snapshot.Duration)}" : string.Empty,
			new UiProgressReference
			{
				PositionMs = (long)Math.Clamp(snapshot.Position.TotalMilliseconds, 0, double.MaxValue),
				Anchor = DateTimeOffset.UtcNow,
				DurationMs = durationMs,
				Rate = snapshot.Status == PlaybackStatus.Playing ? 1 : 0,
			},
			snapshot.Status == PlaybackStatus.Playing,
			snapshot.HasSession,
			options,
			accent,
			accentDark);
	}
}

internal static class NowPlayingView
{
	public static UiElement Build(
		UiState<WidgetContent> content,
		Func<string, CancellationToken, Task>? command)
	{
		var options = content.Peek().Options;
		var media = new List<UiElement>
		{
			new UiTextRun
			{
				Key = "title",
				Text = UiText.From(() => content.Value.Title),
				Size = options.Compact ? UiSize.Capped(0.1, 12) : UiSize.Capped(0.12, 14),
				Align = UiComponentAlignments.Center,
				Wrap = true,
				MaxLines = 2,
			},
			new UiTextRun
			{
				Key = "artist",
				Text = UiText.From(() => content.Value.Artist),
				Size = options.Compact ? UiSize.Capped(0.09, 10) : UiSize.Capped(0.1, 12),
				Align = UiComponentAlignments.Center,
			},
		};

		if (options.ShowAlbum && !options.Compact)
		{
			media.Add(new UiTextRun
			{
				Key = "album",
				Text = UiText.From(() => content.Value.Album),
				Size = UiSize.Capped(0.09, 10),
				Align = UiComponentAlignments.Center,
			});
		}

		if (options.ShowProgress)
		{
			var anchor = content.Peek().Progress;
			var span = anchor.DurationMs is { } total && total > 0
				? Math.Clamp((double)anchor.PositionMs / total, 0, 1)
				: 0;
			media.Add(new UiProgressBar
			{
				Key = "progress",
				Value = UiValue.From(() => content.Value.Progress),
				StartColor = UiValue.Optional(() => string.IsNullOrEmpty(content.Value.Accent)
					? UiValue.None<string>()
					: UiValue.Of(content.Value.Accent)),
				EndColor = UiValue.Optional(() => string.IsNullOrEmpty(content.Value.Accent)
					? UiValue.None<string>()
					: UiValue.Of(content.Value.Accent)),
				Thickness = 0.05,
				Fallback = new UiRangeBar
				{
					Key = "progress-fallback",
					Start = UiValue.Of(0.0),
					End = UiValue.Of(span),
					Thickness = 0.05,
				},
			});
			if (!options.Compact)
			{
				media.Add(new UiTextRun
				{
					Key = "progress-text",
					Text = UiText.From(() => content.Value.ProgressText),
					Size = UiSize.Capped(0.09, 10),
					Align = UiComponentAlignments.Center,
				});
			}
		}

		if (options.ShowControls)
		{
			media.Add(new UiStack
			{
				Key = "controls",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Gap = 0.04,
				Children =
				[
					ControlButton("previous", Strings.Widget.Symbols.Previous(), "previous", content, command),
					ControlButton("play", Strings.Widget.Symbols.Play(), "toggle", content, command),
					ControlButton("next", Strings.Widget.Symbols.Next(), "next", content, command),
				],
			});
		}

		var children = new List<UiElement>
		{
			new UiWhen
			{
				Key = "empty",
				Condition = () => !content.Value.HasMedia,
				Content = () => new UiTextRun
				{
					Key = "empty-label",
					Text = UiText.FromLocalized(() => Strings.Widget.NothingPlaying()),
					Size = 0.13,
					Align = UiComponentAlignments.Center,
				},
			},
			new UiWhen
			{
				Key = "media",
				Condition = () => content.Value.HasMedia,
				Content = () => new UiFragment { Key = "media-rows", Children = media },
			},
		};

		return new UiStack
		{
			Key = "now-playing",
			Padding = 0.06,
			Gap = 0.04,
			Background = UiValue.Optional(() => string.IsNullOrEmpty(content.Value.AccentDark)
				? UiValue.None<string>()
				: UiValue.Of(content.Value.AccentDark)),
			Children = children,
		};
	}

	private static UiButton ControlButton(
		string key,
		MacroDeck.Localization.LocalizedString label,
		string command,
		UiState<WidgetContent> content,
		Func<string, CancellationToken, Task>? handler)
	{
		var button = new UiButton
		{
			Key = key,
			Justify = UiComponentJustify.Center,
			Fill = true,
			Corner = UiComponentButtonCorners.Tile,
			BorderStyle = UiComponentBorderStyles.Static,
			Children =
			[
				new UiTextRun
				{
					Key = key + "-label",
					Text = UiText.FromLocalized(() => ResolveLabel(key, content.Value, label)),
					Size = 0.14,
					Align = UiComponentAlignments.Center,
				},
			],
		};

		if (handler is not null)
		{
			var press = handler;
			var action = command;
			button = button with { Events = [UiEventHandler.OnAsync(UiComponentEvents.Press, ct => press(action, ct))] };
		}

		return button;
	}

	private static MacroDeck.Localization.LocalizedString ResolveLabel(
		string key,
		WidgetContent content,
		MacroDeck.Localization.LocalizedString fallback) =>
		key == "play" && content.IsPlaying ? Strings.Widget.Symbols.Pause() : fallback;
}

internal static class NowPlayingPreviews
{
	[UiPreview("Playing", View = "NowPlaying", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Playing() => NowPlayingView.Build(
		new UiState<WidgetContent>(new WidgetContent(
			"Nightcall", "Kavinsky", "OutRun", "0:42 / 3:35",
			new UiProgressReference { PositionMs = 42000, Anchor = DateTimeOffset.UtcNow, DurationMs = 215000, Rate = 1 },
			true, true, WidgetOptions.Default, "#1DB954", "#07451B")),
		null);

	[UiPreview("Nothing playing", View = "NowPlaying", Profile = UiPreviewProfiles.Widget)]
	public static UiElement NothingPlaying() => NowPlayingView.Build(
		new UiState<WidgetContent>(WidgetContent.Empty),
		null);
}

public sealed class NowPlayingWidget : IWidgetTypeProvider, IUiProvider
{
	private readonly IMediaControlService _media;
	private readonly Serilog.ILogger _logger;
	private static readonly object s_registrationGate = new();
	private static readonly WidgetTypeDescriptor s_descriptor = new(
		"now-playing",
		Strings.Widget.NowPlaying.Name(),
		Strings.Widget.NowPlaying.Description(),
		"""{"showAlbum":true,"showProgress":true,"showControls":true,"compactMode":false}""",
		"""{"type":"object","properties":{"showAlbum":{"type":"boolean"},"showProgress":{"type":"boolean"},"showControls":{"type":"boolean"},"compactMode":{"type":"boolean"}}}""",
		true,
		new Dictionary<string, string>());
	private static string? s_widgetTypeId;
	private static bool s_registered;

	public NowPlayingWidget(IMediaControlService media, Serilog.ILogger logger)
	{
		_media = media;
		_logger = logger.ForContext<NowPlayingWidget>();
	}

	public string ProviderName => "Windows media";

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
	];

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken)
	{
		lock (s_registrationGate)
		{
			if (s_registered)
			{
				return;
			}
		}

		const int maxAttempts = 5;
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				WidgetTypeRegistration registration = await context.RegisterWidgetTypeAsync(s_descriptor, cancellationToken);
				lock (s_registrationGate)
				{
					s_widgetTypeId = registration.WidgetTypeId;
					s_registered = true;
				}

				return;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex) when (attempt < maxAttempts)
			{
				_logger.Debug(ex, "Widget registration attempt {Attempt} failed, retrying.", attempt);
				await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
			}
		}
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => [s_descriptor];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		var surface = request.Surface;
		if (surface.Kind is UiSurfaceKinds.Widget or UiSurfaceKinds.Preview)
		{
			if (s_widgetTypeId is not null
				&& ReadString(surface, UiWidgetSurfaceAttributes.WidgetType) is string widgetType
				&& widgetType != s_widgetTypeId)
			{
				return null;
			}

			var options = WidgetOptions.FromData(ReadElement(surface, UiWidgetSurfaceAttributes.Data));
			if (ReadBool(surface, UiWidgetSurfaceAttributes.Sample) == true)
			{
				return new NowPlayingSession(
					surface,
					new UiState<WidgetContent>(new WidgetContent(
						"Nightcall", "Kavinsky", "OutRun", "0:42 / 3:35",
						new UiProgressReference { PositionMs = 42000, Anchor = DateTimeOffset.UtcNow, DurationMs = 215000, Rate = 1 },
						true, true, options, "#1DB954", "#07451B")),
					_media,
					_logger,
					live: false);
			}

			var snapshot = await _media.GetSnapshotAsync(cancellationToken);
			var cached = _media.TryGetCachedArtwork(snapshot.ArtworkId);
			return new NowPlayingSession(
				surface,
				new UiState<WidgetContent>(WidgetContent.FromSnapshot(snapshot, options, cached?.Accent ?? string.Empty, cached?.AccentDark ?? string.Empty)),
				_media,
				_logger,
				live: true);
		}

		if (surface.Kind == UiSurfaceKinds.Config
			&& ReadString(surface, UiConfigSurfaceAttributes.EntryPoint) == UiConfigEntryPoints.WidgetConfig)
		{
			var data = ReadElement(surface, UiConfigSurfaceAttributes.WidgetData);
			var options = WidgetOptions.FromData(data);
			var showAlbum = new UiState<bool>(options.ShowAlbum);
			var showProgress = new UiState<bool>(options.ShowProgress);
			var showControls = new UiState<bool>(options.ShowControls);
			var compactMode = new UiState<bool>(options.Compact);
			var view = new UiView(surface, new UiWidgetConfiguration
			{
				Key = "config",
				Properties = new UiWidgetProperties
				{
					Key = "properties",
					Children =
					[
						new UiBooleanInput
						{
							Key = "showAlbum",
							Label = Strings.Widget.Config.ShowAlbum(),
							Binding = Bind.To(showAlbum),
						},
						new UiBooleanInput
						{
							Key = "showProgress",
							Label = Strings.Widget.Config.ShowProgress(),
							Binding = Bind.To(showProgress),
						},
						new UiBooleanInput
						{
							Key = "showControls",
							Label = Strings.Widget.Config.ShowControls(),
							Binding = Bind.To(showControls),
						},
						new UiBooleanInput
						{
							Key = "compactMode",
							Label = Strings.Widget.Config.CompactMode(),
							Binding = Bind.To(compactMode),
						},
					],
				},
			});
			return new NowPlayingSession(view);
		}

		return null;
	}

	private static JsonElement ReadElement(MacroDeck.Ui.Model.Surfaces.UiSurface surface, string key) =>
		surface.Attributes.TryGetValue(key, out var element) ? element : default;
	private static string? ReadString(MacroDeck.Ui.Model.Surfaces.UiSurface surface, string key)
	{
		var element = ReadElement(surface, key);
		return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
	}

	private static bool? ReadBool(MacroDeck.Ui.Model.Surfaces.UiSurface surface, string key)
	{
		var element = ReadElement(surface, key);
		return element.ValueKind is JsonValueKind.True or JsonValueKind.False ? element.GetBoolean() : null;
	}

	private sealed class NowPlayingSession : IUiSession, IDisposable, IAsyncDisposable
	{
		private readonly UiView _view;
		private readonly UiState<WidgetContent>? _content;
		private readonly IMediaControlService? _media;
		private readonly Serilog.ILogger? _logger;
		private readonly CancellationTokenSource _cts = new();
		private readonly Task? _loop;
		private bool _disposed;

		public NowPlayingSession(
			MacroDeck.Ui.Model.Surfaces.UiSurface surface,
			UiState<WidgetContent> content,
			IMediaControlService media,
			Serilog.ILogger logger,
			bool live)
		{
			_content = content;
			_media = media;
			_logger = logger;
			Func<string, CancellationToken, Task>? command = live ? HandleCommandAsync : null;
			_view = new UiView(surface, NowPlayingView.Build(content, command));
			_view.Changed += OnChanged;
			_view.HandlerFaulted += OnHandlerFaulted;
			if (live)
			{
				_loop = RefreshLoopAsync(_cts.Token);
			}
		}

		public NowPlayingSession(UiView view)
		{
			_view = view;
			_view.Changed += OnChanged;
			_view.HandlerFaulted += OnHandlerFaulted;
		}

		public event EventHandler? Changed;

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

		public UiTree BuildTree() => _view.Tree;

		public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

		public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_cts.Cancel();
			_cts.Dispose();
			_view.Changed -= OnChanged;
			_view.HandlerFaulted -= OnHandlerFaulted;
		}

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}

		private async Task HandleCommandAsync(string command, CancellationToken cancellationToken)
		{
			if (_media is null || _content is null)
			{
				return;
			}

			try
			{
				var ok = command switch
				{
					"previous" => await _media.PreviousAsync(cancellationToken),
					"next" => await _media.NextAsync(cancellationToken),
					_ => await _media.TogglePlayPauseAsync(cancellationToken),
				};
				if (ok)
				{
					await RefreshAsync(cancellationToken);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				_logger?.Debug(ex, "Widget command failed.");
			}
		}

		private async Task RefreshLoopAsync(CancellationToken cancellationToken)
		{
			using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					await timer.WaitForNextTickAsync(cancellationToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}

				try
				{
					await RefreshAsync(cancellationToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					_logger?.Debug(ex, "Widget refresh failed.");
				}
			}
		}

		private async Task RefreshAsync(CancellationToken cancellationToken)
		{
			if (_media is null || _content is null)
			{
				return;
			}

			var snapshot = await _media.GetSnapshotAsync(cancellationToken);
			if (!string.IsNullOrEmpty(snapshot.ArtworkId))
			{
				try
				{
					await _media.GetArtworkAsync(snapshot.ArtworkId, cancellationToken);
				}
				catch (Exception ex)
				{
					_logger?.Debug(ex, "Widget artwork warmup failed.");
				}
			}

			var cached = _media.TryGetCachedArtwork(snapshot.ArtworkId);
			_content.Set(WidgetContent.FromSnapshot(
				snapshot, _content.Value.Options, cached?.Accent ?? string.Empty, cached?.AccentDark ?? string.Empty));
		}

		private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, e);

		private void OnHandlerFaulted(object? sender, UiHandlerFaultEventArgs e) =>
			Faulted?.Invoke(this, new UiSessionFaultedEventArgs("handler-fault", e.Exception));
	}
}
