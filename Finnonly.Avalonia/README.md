# Finnonly.Avalonia

Avalonia extensions for event binding using Source Generators.

## Features

- **RoutedEvent** - Bind routed events directly in XAML
- **RawEvent** - Handle non-EventArgs events (like WindowClosing)
- **Attributes** - EventBind, CopyTo, Compare, Table, Column, PrimaryKey, etc.

## Installation

```xml
<PackageReference Include="Finnonly.Avalonia" Version="1.0.0" />
<PackageReference Include="Finnonly.SourceGenerator" Version="1.0.0" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Usage

```xml
<!-- In XAML -->
<Window xmlns:v="clr-namespace:Finnonly.Avalonia;assembly=Finnonly.Avalonia"
        Loaded="{v:RoutedEvent Loaded}"
        Closing="{v:RawEvent WindowClosing}">
```

```csharp
// In ViewModel
[EventBind]
public void Loaded() { }

[EventBind]
public void WindowClosing() { }
```

## License

MIT
