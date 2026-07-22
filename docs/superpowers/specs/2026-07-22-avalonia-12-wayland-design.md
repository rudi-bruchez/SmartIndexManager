# Mise à niveau Avalonia 12 + support Wayland

## Objectif

Permettre au client Linux de fonctionner nativement sous Wayland afin de respecter le scaling fractionnaire des bureaux modernes, au lieu de passer par XWayland. Cela corrige le texte petit constaté sur les sessions Wayland.

## Contraintes

- Le projet cible actuellement Avalonia 11.3.18, qui ne possède pas de backend Wayland.
- Le paquet `Avalonia.Wayland` n'existe qu'à partir d'Avalonia 12.1.0.
- Il faut donc monter tout l'écosystème Avalonia en version 12.1.0.

## Modifications prévues

### Paquets NuGet

Dans `src/SmartIndexManager.App/SmartIndexManager.App.csproj` :

| Paquet | Actuel | Cible |
| --- | --- | --- |
| Avalonia | 11.3.18 | 12.1.0 |
| Avalonia.Desktop | 11.3.18 | 12.1.0 |
| Avalonia.Fonts.Inter | 11.3.18 | 12.1.0 |
| Avalonia.Controls.DataGrid | 11.3.13 | 12.1.0 |
| Avalonia.Wayland | absent | 12.1.0 |
| Semi.Avalonia | 11.3.* | 12.1.0 |
| Material.Icons.Avalonia | 2.1.12 | 3.0.2 |

`Microsoft.Extensions.DependencyInjection` et `CommunityToolkit.Mvvm` restent inchangés.

### Code

Dans `src/SmartIndexManager.App/Program.cs`, remplacer `.UsePlatformDetect()` par une détection conditionnelle Wayland.

`UseWaylandWithFallback()` n'existe pas dans `Avalonia.Wayland` 12.1.0 ; la méthode équivalente n'est apparue que dans les versions ultérieures. Le code ajoute donc une extension `UseWaylandIfAvailable()` qui :

- sous Linux, vérifie la présence de la variable `WAYLAND_DISPLAY` ;
- si elle est définie, appelle `UseWayland()` ;
- sinon conserve le backend X11 configuré par `UsePlatformDetect()`.

Sur Windows et macOS, l'extension ne fait rien.

### Ajustements post-migration

Avalonia 12 comporte des changements cassants par rapport à la version 11. La démarche est :

1. Lancer `dotnet build`.
2. Corriger les erreurs de compilation une par une (API de thème, propriétés de contrôles, espaces de noms, etc.).
3. S'assurer que `dotnet test` reste vert.
4. Vérifier visuellement que l'interface démarre et que les styles Semi sont appliqués.

## Tests et validation

- `dotnet build SmartIndexManager.sln` sans erreur ni avertissement.
- `dotnet test` : 233 tests passés.
- Lancement de l'application via `dotnet run --project src/SmartIndexManager.App` sous Linux pour vérifier le rendu.

## Risques

- `Semi.Avalonia` 12.1.0 ou `Material.Icons.Avalonia` 3.0.2 peuvent avoir des changements de style ou d'API à corriger.
- Avalonia 12 est une version majeure ; des ajustements XAML ou C# sont attendus.
