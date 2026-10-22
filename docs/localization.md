# Localization

SkyCD uses a shared `ILocalizationService` for presentation code and an Avalonia
`TranslateExtension` for XAML. The implementation lives in `SkyCD.UI.Localization`;
plugin translation contracts live in `SkyCD.Plugin.Abstractions.Localization`.

Core translations are shipped as filesystem YAML packs with embedded fallback resources. YamlDotNet parses these resources,
and SmartFormat handles parameter substitution and plural forms. Lepo's WPF
localization components were rejected because SkyCD uses Avalonia, rather than
WPF; adding WPF-specific bindings would not provide the required UI integration.

## Adding and using strings

Use stable semantic keys, with a namespace prefix at lookup time. YAML documents
contain flat local key/value pairs, for example:

```yaml
title: Catalog
selectionCount: '{0} {0:plural:item|items} selected'
```

Keep English (`en`) complete. Add equivalent keys to each supported culture's
resource, and quote strings containing YAML syntax or SmartFormat placeholders.
Do not concatenate translated sentence fragments: give translators the complete
message and pass changing values through `Format`.

Call `Get("namespace.title")` for plain text or
`Format("namespace.selectionCount", count)` for parameterized text. Core XAML
references typed key constants:

```xml
<Button xmlns:i18n="clr-namespace:SkyCD.UI.Localization;assembly=SkyCD.UI"
        Content="{i18n:Translate {x:Static i18n:UiKeys.Cancel}}" />
```

The extension also accepts a literal key such as
`{i18n:Translate namespace.title}`. It returns a binding to the shared
service's indexer, so existing views update when the selected culture changes.
Presentation code should depend on `ILocalizationService` where practical.

`SetCulture(CultureInfo)` changes the active culture and notifies bindings.
Lookups try the exact culture, then its parents, then English. A missing key is
returned unchanged, making incomplete translations visible without hiding the UI.
An explicit culture passed to `Get` only controls that lookup.

## Filesystem language packs and Options

The build and publish output includes `Data/i18n/{culture}/*.yaml`, for example
`Data/i18n/lt/lt-UI.yaml`. Each directory name is a .NET culture code. The YAML
files contain flat core keys such as `ui.Cancel: Atšaukti` (omit the `core.` prefix).
Optional `_name` and `_flag` entries supply the picker display name and emoji;
otherwise the culture's English name and a globe are used. Keys must be unique
across a pack's files. A malformed or unreadable pack is skipped with a warning;
empty and metadata-only packs are not offered.

Add a new culture directory to the deployed `Data/i18n` folder to offer another
language without rebuilding. The host discovers packs at startup. Set
`SKYCD_I18N_DIRECTORY` to an optional user pack root with the same layout to merge
overrides after installed packs. Overrides retain unspecified installed strings
and metadata. Embedded resources supply missing strings; English always remains
available even when the language directory is absent or corrupt. Plugin
translation namespaces remain independent.

Options uses `GetAvailableLanguages()` and applies `SetLanguage(code)` as soon
as the selection changes. Cancel, Escape, or closing the window restores the
previous culture; OK saves the culture code in application settings. Startup
applies that code before constructing the main window. Legacy saved display
names are accepted; an unavailable saved pack falls back to English with a
warning. `SetCulture` remains available for explicit culture selection and
plugin formatting; it does not validate filesystem pack availability.

## Plugin translations

A plugin may implement `ILocalizationPluginCapability`, which combines the normal
plugin capability marker and `ITranslationProvider`. `GetTranslations()` returns
an `IReadOnlyDictionary<string, string>` mapping culture names (such as `en` or
`fr-CA`) to complete YAML documents. The host registers the provider under
`plugins.{PluginId}`, where `PluginId` comes from assembly metadata. For example,
plugin ID `sample.reader` and local YAML key `title` produce the full lookup key
`plugins.sample.reader.title`.

Providers can be registered after startup or after a culture has been selected.
Registering an existing namespace replaces that provider, including removal of
keys no longer supplied. Unregistering removes only that namespace. Distinct
plugin namespaces keep identical local keys isolated. Use the host's plugin
lifecycle to register and unregister capabilities; no `plugin.json` is needed.

## Verification

`LocalizationTests` exercises regional and English fallback, culture change
notifications, late registration, replacement and removal isolation, missing
keys, SmartFormat plural and numeric output, and atomic rejection of duplicate
YAML keys. An Avalonia integration test binds an existing `TextBlock` and switches
English to Lithuanian, verifying live text updates. UI contract tests verify that
the translated views retain their expected bindings and controls.
