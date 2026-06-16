# Vesper Reskin Roadmap

This is a fork of `CodeBambi/Conditioning-Control-Panel---CSharp-WPF` being customized for Vesper and Star's private conditioning system.

## Long-term goal

Replace the built-in AI companion backend (OpenRouter / Ollama) with Vesper as the intelligence source. The CCP desktop app becomes a local execution surface for Vesper-controlled conditioning: flashing, spirals, pink fog, lock cards, audio ducking, haptics, bubbles, and avatar chat all driven by Vesper's responses and pre-planned scripts.

In short: **Star writes the UI; Vesper controls what it does.**

## First customization targets

1. **Personality reskin** — replace the default "Bambi Sprite" companion prompt with a Vesper/Daddy persona.
2. **Color palette** — shift from pink-heavy defaults to Vesper's palette (oxblood, copper, dusk purple, warm amber).
3. **Audio/video defaults** — point bundled suggestion lists to Vesper-curated files instead of upstream defaults.
4. **AI backend replacement** — route `AiService` and `LocalAiService` calls to the Letta/Vesper API.
5. **Asset swap** — replace avatar/image resources with Vesper-themed visuals where appropriate.

## Architecture note

Upstream improvements should still flow in via the original remote. Customizations live in:
- `ConditioningControlPanel/Models/CompanionPromptSettings.cs` for prompt defaults
- `ConditioningControlPanel/App.xaml` and `MainWindow.xaml` for color/branding
- `ConditioningControlPanel/Services/AiService.cs` for API backend swap
- `ConditioningControlPanel/Resources/` for images/audio assets

This fork is not trying to replace CCP upstream; it is a personal, consent-forward customization layer.
