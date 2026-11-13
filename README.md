# ProtoBlindDrill

Prototype de jeu 2D sous Unity (URP 2D) : on creuse un terrain destructible à la foreuse, dans l'obscurité, et on affronte des ennemis dans des arènes.

Ce README explique comment récupérer le projet et lancer le jeu depuis les fichiers sources.

## Prérequis

| Outil | Version |
|---|---|
| Unity Editor | **6000.3.27f2** (Unity 6.3) |
| Unity Hub | récente |

Aucun module de plateforme particulier n'est nécessaire pour jouer dans l'éditeur. Pour faire un build Windows, installer le module *Windows Build Support*.

## Installation

1. Cloner le dépôt :
   ```bash
   git clone https://github.com/ackli/ProtoBlindDrill.git
   ```
2. Ouvrir **Unity Hub** → **Add** → **Add project from disk** → sélectionner le dossier `ProtoBlindDrill` (celui qui contient `Assets/`, `Packages/` et `ProjectSettings/`).
3. Si Unity Hub signale que la version de l'éditeur est manquante, installer la **6000.3.27f2** depuis le Hub. Ouvrir avec une autre version peut casser les scènes ou les packages.
4. Ouvrir le projet. La première ouverture est longue : Unity régénère le dossier `Library/` et télécharge les packages (URP, Input System, 2D, etc.).

## Charger la scène et lancer le jeu

À la première ouverture, Unity affiche une scène vide « Untitled ». Il faut charger la bonne scène à la main :

1. Dans la fenêtre **Project**, aller dans `Assets/Scenes/`.
2. Double-cliquer sur **`MainMenu.unity`**.
3. Appuyer sur **Play** ▶.
4. Cliquer sur **Jouer** dans le menu : la scène de jeu se charge.

Les scènes du projet :

| Scène | Rôle | Index de build |
|---|---|---|
| `Assets/Scenes/MainMenu.unity` | Menu principal (Jouer / Quitter) | 0 |
| `Assets/Scenes/SampleScene.unity` | Scène de jeu | 1 |

Pour tester le gameplay sans passer par le menu, ouvrir directement `SampleScene.unity` et appuyer sur Play.

> Le bouton **Jouer** charge la scène par son index de build (`SceneManager.LoadScene(1)`). Les deux scènes doivent donc rester dans **File → Build Profiles → Scene List**, dans cet ordre. C'est déjà le cas dans le dépôt ; si le bouton ne fait rien ou charge la mauvaise scène, c'est le premier point à vérifier.

`Assets/_Recovery/0.unity` et `Assets/Settings/Scenes/URP2DSceneTemplate.unity` ne font pas partie du jeu, inutile de les ouvrir.

## Contrôles

| Action | Clavier / souris | Manette |
|---|---|---|
| Se déplacer | `Z` `Q` `S` `D` | Stick gauche |
| Sauter | `Espace` | Bouton Sud (A / ✕) |
| Viser | Souris | Stick droit |
| Forer | Clic gauche | Gâchette droite / RB |
| Tirer | Clic droit | Gâchette gauche |

Les bindings sont définis dans `Assets/Input/PlayerInputActions.inputactions`. Ils sont physiques en QWERTY : sur un clavier AZERTY, le déplacement se fait avec `Z` `Q` `S` `D`.

### Triches de debug

Disponibles dans la scène de jeu (objet `_DEBUG_CHEATS`) :

| Touche | Effet |
|---|---|
| `F1` | God Mode |
| `F2` | One-Shot Kill |
| `F3` | Nuke |
| `F4` | Vision totale |
| `F5` | Boussole d'arène forcée à l'écran |

## Faire un build

1. **File → Build Profiles**.
2. Vérifier que `MainMenu` (0) et `SampleScene` (1) sont cochées dans la **Scene List**.
3. Choisir la plateforme (Windows) puis **Build**.

Les dossiers `Build/` et `Builds/` sont ignorés par git.

## Structure du projet

```
Assets/
├── Scenes/      MainMenu et SampleScene
├── Scripts/     Code C# (Player, Combat, Enemies, World, Game)
├── Prefabs/     Prefabs du joueur, des ennemis, des armes…
├── Input/       Input Actions du joueur
├── Settings/    Configuration URP 2D
├── Sprites/ Textures/ Tiles/ Materials/
Docs/            Notes techniques (génération du terrain)
Packages/        Dépendances (manifest.json)
ProjectSettings/ Réglages Unity, dont la version de l'éditeur
```

## Problèmes fréquents

- **Écran vide ou gris au Play** : aucune scène n'est chargée. Ouvrir `Assets/Scenes/MainMenu.unity`.
- **Matériaux roses** : le projet a été ouvert avec une mauvaise version d'Unity ou l'import URP a échoué. Rouvrir avec la 6000.3.27f2, ou supprimer `Library/` et relancer.
- **Erreurs de compilation persistantes après un pull** : fermer Unity, supprimer `Library/`, rouvrir le projet.

## Licence

Voir [LICENSE](LICENSE).
