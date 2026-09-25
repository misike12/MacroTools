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
using MacroDeck.Ui.Model.Resources;
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
	UiProgressReference Progress,
	bool IsPlaying,
	bool HasMedia,
	WidgetOptions Options,
	string Accent,
	string AccentDark,
	UiResource? Cover = null)
{
	public static WidgetContent Empty { get; } = new(
		string.Empty, string.Empty, string.Empty,
		new UiProgressReference { PositionMs = 0, Anchor = DateTimeOffset.UtcNow },
		false, false, WidgetOptions.Default, string.Empty, string.Empty);

	public static WidgetContent FromSnapshot(MediaSnapshot snapshot, WidgetOptions options, string accent, string accentDark)
	{
		var durationMs = snapshot.Duration > TimeSpan.Zero ? (long?)snapshot.Duration.TotalMilliseconds : null;
		return new WidgetContent(
			snapshot.Title,
			snapshot.Artist,
			snapshot.Album,
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
		Func<string, CancellationToken, Task>? command,
		UiWidgetAppearanceValues? appearance = null)
	{
		var options = content.Peek().Options;
		var media = new List<UiElement>();

		var appearanceLabel = appearance?.Label;
		if (!string.IsNullOrWhiteSpace(appearanceLabel))
		{
			var appearanceLabelColor = appearance?.LabelColor;
			media.Add(new UiTextRun
			{
				Key = "appearance-label",
				Text = UiText.From(() => appearanceLabel),
				Size = UiSize.Capped(0.09, 11),
				Weight = UiComponentTextWeights.Medium,
				Color = string.IsNullOrWhiteSpace(appearanceLabelColor) ? UiValue.None<string>() : UiValue.Of(appearanceLabelColor),
				Align = UiComponentAlignments.Center,
			});
		}

		if (options.ShowAlbum && !options.Compact)
		{
			media.Add(new UiWhen
			{
				Key = "cover-when",
				Condition = () => content.Value.Cover is not null,
				Content = () => new UiImage
				{
					Key = "cover",
					Source = UiValue.From(() => content.Value.Cover!),
					Size = 0.3,
				},
			});
		}

		media.Add(new UiTextRun
		{
			Key = "title",
			Text = UiText.From(() => content.Value.Title),
			Size = options.Compact ? UiSize.Capped(0.1, 12) : UiSize.Capped(0.125, 15),
			Weight = UiComponentTextWeights.SemiBold,
			Align = UiComponentAlignments.Center,
			Wrap = true,
			MaxLines = 2,
		});

		media.Add(new UiWhen
		{
			Key = "artist-when",
			Condition = () => !string.IsNullOrWhiteSpace(content.Value.Artist),
			Content = () => new UiTextRun
			{
				Key = "artist",
				Text = UiText.From(() => content.Value.Artist),
				Size = options.Compact ? UiSize.Capped(0.09, 10) : UiSize.Capped(0.1, 12),
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Center,
			},
		});

		if (options.ShowAlbum && !options.Compact)
		{
			media.Add(new UiWhen
			{
				Key = "album-when",
				Condition = () => !string.IsNullOrWhiteSpace(content.Value.Album),
				Content = () => new UiTextRun
				{
					Key = "album",
					Text = UiText.From(() => content.Value.Album),
					Size = UiSize.Capped(0.085, 10),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
			});
		}

		if (options.ShowProgress)
		{
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
				Thickness = 0.04,
				Fallback = new UiRangeBar
				{
					Key = "progress-fallback",
					Start = UiValue.Of(0.0),
					End = UiValue.From(() => FallbackFrac(content.Value.Progress)),
					Thickness = 0.04,
				},
			});
			if (!options.Compact)
			{
				media.Add(new UiStack
				{
					Key = "times",
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.SpaceBetween,
					Children =
					[
						new UiProgressText
						{
							Key = "elapsed",
							Format = UiProgressFormats.Elapsed,
							Value = UiValue.From(() => content.Value.Progress),
							Size = UiSize.Capped(0.08, 10),
							Role = UiComponentTextRoles.Muted,
							// A reader without macrodeck.progress-text draws the
							// anchor second instead of a ticking clock.
							Fallback = new UiTextRun
							{
								Key = "elapsed-fallback",
								Text = UiText.From(() => FormatAnchorMs(content.Value.Progress.PositionMs)),
								Size = UiSize.Capped(0.08, 10),
								Role = UiComponentTextRoles.Muted,
							},
						},
						new UiProgressText
						{
							Key = "duration",
							Format = UiProgressFormats.Duration,
							Value = UiValue.From(() => content.Value.Progress),
							Size = UiSize.Capped(0.08, 10),
							Role = UiComponentTextRoles.Muted,
							Fallback = new UiTextRun
							{
								Key = "duration-fallback",
								Text = UiText.From(() => FormatAnchorMs(content.Value.Progress.DurationMs)),
								Size = UiSize.Capped(0.08, 10),
								Role = UiComponentTextRoles.Muted,
							},
						},
					],
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
				Gap = 0.05,
				Children =
				[
					ControlButton("previous", Strings.Widget.Symbols.Previous(), "previous", side: true, content, command),
					ControlButton("play", Strings.Widget.Symbols.Play(), "toggle", side: false, content, command),
					ControlButton("next", Strings.Widget.Symbols.Next(), "next", side: true, content, command),
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

		var background = appearance?.BackgroundColor;
		return new UiStack
		{
			Key = "now-playing",
			Padding = 0.07,
			Gap = 0.045,
			Background = UiValue.Optional(() => !string.IsNullOrWhiteSpace(background)
				? UiValue.Of(background)
				: string.IsNullOrEmpty(content.Value.AccentDark)
					? UiValue.None<string>()
					: UiValue.Of(content.Value.AccentDark)),
			Children = children,
		};
	}

	private static UiButton ControlButton(
		string key,
		MacroDeck.Localization.LocalizedString label,
		string command,
		bool side,
		UiState<WidgetContent> content,
		Func<string, CancellationToken, Task>? handler)
	{
		var button = new UiButton
		{
			Key = key,
			Justify = UiComponentJustify.Center,
			Fill = !side,
			MainSize = side ? UiSize.Capped(0.2, 48) : default,
			Corner = UiComponentButtonCorners.Tile,
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

	// Anchor rendering for readers without macrodeck.progress-text: the right
	// picture at the anchor second, refreshed on every re-anchor.
	private static string FormatAnchorMs(long? ms)
	{
		if (ms is not { } value || value < 0)
		{
			return string.Empty;
		}

		var total = (int)(value / 1000);
		return $"{total / 60}:{total % 60:D2}";
	}

		private static double FallbackFrac(UiProgressReference progress)
	{
		if (progress.DurationMs is not { } total || total <= 0)
		{
			return 0;
		}

		return Math.Clamp((double)progress.PositionMs / total, 0, 1);
	}
}

internal static class NowPlayingPreviews
{
	[UiPreview("Playing", View = "NowPlaying", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Playing() => NowPlayingView.Build(
		new UiState<WidgetContent>(new WidgetContent(
			"Nightcall", "Kavinsky", "OutRun",
			new UiProgressReference { PositionMs = 42000, Anchor = DateTimeOffset.UtcNow, DurationMs = 215000, Rate = 1 },
			true, true, WidgetOptions.Default, "#1DB954", "#07451B")),
		null);

	[UiPreview("Nothing playing", View = "NowPlaying", Profile = UiPreviewProfiles.Widget)]
	public static UiElement NothingPlaying() => NowPlayingView.Build(
		new UiState<WidgetContent>(WidgetContent.Empty),
		null);

	// Test and tooling support: builds the live tree against caller-owned
	// state so patches can be observed without a running session.
	public static UiElement FromState(UiState<WidgetContent> state) =>
		NowPlayingView.Build(state, null);
}

public sealed class NowPlayingWidget : IWidgetTypeProvider, IUiProvider
{
	private readonly IMediaControlService _media;
	private readonly Serilog.ILogger _logger;
	private readonly Func<MediaSnapshot>? _snapshots;
	private readonly Func<IUiResourceRegistry?>? _resources;
	private static readonly object s_registrationGate = new();
	private static readonly WidgetTypeDescriptor s_descriptor = new(
		"now-playing",
		Strings.Widget.NowPlaying.Name(),
		Strings.Widget.NowPlaying.Description(),
		"""{"showAlbum":true,"showProgress":true,"showControls":true,"compactMode":false,"backgroundColor":"","label":"","labelColor":"","flows":[]}""",
		"""{"type":"object","properties":{"showAlbum":{"type":"boolean"},"showProgress":{"type":"boolean"},"showControls":{"type":"boolean"},"compactMode":{"type":"boolean"},"backgroundColor":{"type":"string"},"label":{"type":"string"},"labelColor":{"type":"string"},"flows":{"type":"array"}}}""",
		true,
		new Dictionary<string, string>())
	{
		SupportsFlows = true,
		AppearanceProperties =
		[
			WidgetAppearanceProperty.BackgroundColor,
			WidgetAppearanceProperty.Label,
			WidgetAppearanceProperty.LabelColor,
		],
	};
	private static string? s_widgetTypeId;
	private static bool s_registered;

	public NowPlayingWidget(IMediaControlService media, Serilog.ILogger logger, Func<MediaSnapshot>? snapshots = null, Func<IUiResourceRegistry?>? resources = null)
	{
		_media = media;
		_logger = logger.ForContext<NowPlayingWidget>();
		_snapshots = snapshots;
		_resources = resources;
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

			var data = ReadElement(surface, UiWidgetSurfaceAttributes.Data);
			var options = WidgetOptions.FromData(data);
			var appearance = UiWidgetAppearance.Read(data);
			if (ReadBool(surface, UiWidgetSurfaceAttributes.Sample) == true)
			{
				return new NowPlayingSession(
					surface,
					new UiState<WidgetContent>(new WidgetContent(
						"Nightcall", "Kavinsky", "OutRun",
						new UiProgressReference { PositionMs = 42000, Anchor = DateTimeOffset.UtcNow, DurationMs = 215000, Rate = 1 },
						true, true, options, "#1DB954", "#07451B")),
					_media,
					snapshots: null,
					_logger,
					live: false,
					appearance,
					_resources);
			}

		// Seed from the integration's warm snapshot when it is fresh: the loop
		// repaints within one tick anyway, so a full SMTC round trip here only
		// delays the first tree.
		var warm = _snapshots?.Invoke();
		var snapshot = warm is not null && DateTimeOffset.UtcNow - warm.UpdatedAt <= TimeSpan.FromSeconds(5)
			? warm
			: await _media.GetSnapshotAsync(cancellationToken);
		var cached = _media.TryGetCachedArtwork(snapshot.ArtworkId);
			return new NowPlayingSession(
				surface,
				new UiState<WidgetContent>(WidgetContent.FromSnapshot(snapshot, options, cached?.Accent ?? string.Empty, cached?.AccentDark ?? string.Empty)),
				_media,
				_snapshots,
				_logger,
				live: true,
				appearance,
				_resources);
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
			var flows = new UiState<JsonElement>(ReadFlows(data));
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
						UiWidgetAppearance.Section(
							data,
							UiWidgetAppearanceFields.BackgroundColor | UiWidgetAppearanceFields.Label | UiWidgetAppearanceFields.LabelColor),
					],
				},
				Editor = new UiWidgetEditor
				{
					Key = "editor",
					Children = [new UiActionsListEditor { Key = "flows", Binding = Bind.To(flows), CanRun = true }],
				},
			});
		return new NowPlayingSession(view);
	}

		return null;
	}

		private static JsonElement ReadFlows(JsonElement data)
		{
			if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("flows", out var flows))
			{
				return flows;
			}

			// The editor binding must always serialize, so an absent key becomes an
			// empty array rather than an undefined element.
			using var empty = JsonDocument.Parse("[]");
			return empty.RootElement.Clone();
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
		private readonly Func<MediaSnapshot>? _snapshots;
		private readonly Func<IUiResourceRegistry?>? _resources;
		private readonly Serilog.ILogger? _logger;
		private readonly CancellationTokenSource _cts = new();
		private readonly Task? _loop;
		private string? _coverArtworkId;
		private bool _disposed;

		public NowPlayingSession(
			MacroDeck.Ui.Model.Surfaces.UiSurface surface,
			UiState<WidgetContent> content,
			IMediaControlService media,
			Func<MediaSnapshot>? snapshots,
			Serilog.ILogger logger,
			bool live,
			UiWidgetAppearanceValues? appearance = null,
			Func<IUiResourceRegistry?>? resources = null)
		{
			_content = content;
			_media = media;
			_snapshots = snapshots;
			_logger = logger;
			_resources = resources;
			Func<string, CancellationToken, Task>? command = live ? HandleCommandAsync : null;
			_view = new UiView(surface, NowPlayingView.Build(content, command, appearance));
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
			// UiView is disposable since SDK beta.11: disposing detaches it from the
			// state it reads, so a closed session no longer leaks on every write.
			_view.Dispose();
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

			var snapshot = _snapshots is not null
				? _snapshots()
				: await _media.GetSnapshotAsync(cancellationToken);
			if (!string.IsNullOrEmpty(snapshot.ArtworkId) && _media.TryGetCachedArtwork(snapshot.ArtworkId) is null)
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
			var cover = await RefreshCoverAsync(snapshot, cached, _content.Value.Cover, cancellationToken);
			var next = WidgetContent.FromSnapshot(
				snapshot, _content.Value.Options, cached?.Accent ?? string.Empty, cached?.AccentDark ?? string.Empty) with
			{
				Cover = cover,
			};
			if (NeedsRefresh(_content.Value, next))
			{
				_content.Set(next);
			}
		}

		private async Task<UiResource?> RefreshCoverAsync(
			MediaSnapshot snapshot,
			ArtworkData? cached,
			UiResource? current,
			CancellationToken cancellationToken)
		{
			var resources = _resources?.Invoke();
			if (resources is null || string.IsNullOrEmpty(snapshot.ArtworkId))
			{
				return null;
			}

			if (current is not null && string.Equals(_coverArtworkId, snapshot.ArtworkId, StringComparison.Ordinal))
			{
				return current;
			}

			if (cached is null
				|| cached.Data.Length == 0
				|| cached.Data.Length > 2 * 1024 * 1024
				|| !IsSupportedImageType(cached.MimeType))
			{
				return null;
			}

			try
			{
				var handle = await resources.RegisterAsync("cover", cached.Data, cached.MimeType, cancellationToken).ConfigureAwait(false);
				_coverArtworkId = snapshot.ArtworkId;
				return handle;
			}
			catch (Exception ex)
			{
				_logger?.Debug(ex, "Widget cover registration failed.");
				return null;
			}
		}

		private static bool IsSupportedImageType(string mimeType) =>
			mimeType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
				|| mimeType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
				|| mimeType.Equals("image/webp", StringComparison.OrdinalIgnoreCase)
				|| mimeType.Equals("image/gif", StringComparison.OrdinalIgnoreCase);

		// The bar and the times tick on the reader's own clock from the published reference, so
		// pushing a new anchor every tick would only spend a patch to redraw the same second and
		// make the bar stutter on every re-anchor. Re-anchor when the reader's prediction drifts.
		private static bool CoverEquals(UiResource? left, UiResource? right) =>
			left is null
				? right is null
				: right is not null && string.Equals(left.ContentHash, right.ContentHash, StringComparison.Ordinal);

		private static bool NeedsRefresh(WidgetContent current, WidgetContent next)
		{
			if (current.Title != next.Title
				|| current.Artist != next.Artist
				|| current.Album != next.Album
				|| current.IsPlaying != next.IsPlaying
				|| current.HasMedia != next.HasMedia
				|| current.Accent != next.Accent
				|| current.AccentDark != next.AccentDark
				|| !CoverEquals(current.Cover, next.Cover)
				|| current.Progress.DurationMs != next.Progress.DurationMs
				|| current.Progress.Rate != next.Progress.Rate)
			{
				return true;
			}

			var predicted = current.Progress.PositionMs;
			if (current.IsPlaying)
			{
				predicted += (long)(DateTimeOffset.UtcNow - current.Progress.Anchor).TotalMilliseconds;
			}

			return Math.Abs(predicted - next.Progress.PositionMs) > 1500;
		}

		private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, e);

		private void OnHandlerFaulted(object? sender, UiHandlerFaultEventArgs e) =>
			Faulted?.Invoke(this, new UiSessionFaultedEventArgs("handler-fault", e.Exception));
	}
}
