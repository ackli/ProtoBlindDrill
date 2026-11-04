# TerrainGenerator — Paramètres & Fonctionnement

Script : `Assets/Scripts/World/TerrainGenerator.cs`
Dernière mise à jour : 2026-11-04

## Rôle

Génère au `Start` un rectangle de Tilemap rempli de tuiles destructibles, avec :
- Une bordure **Bedrock** incassable (bas, gauche, droite).
- Un fond de **Terre** majoritaire.
- Des patches de **Pierre** (plus résistants) via bruit de Perlin.
- Des **poches d'air** (cellules vides) sculptées par un second bruit de Perlin → force le joueur à creuser d'une cavité à l'autre.
- Une **zone de spawn garantie vide** au haut-centre pour que le joueur ne naisse pas dans un mur.

Le tout est appliqué en un seul `Tilemap.SetTiles(positions, tiles)` → batch unique, rapide.

## Ordre de priorité dans `PickTile(x, y)`

L'ordre est **strict** et ne doit pas être réordonné :

1. **Bordure Bedrock** — si `bedrockTile != null` **et** la cellule est sur la rangée du bas ou les colonnes de gauche/droite → `bedrockTile`. Aucune cavité ne peut la perforer.
2. **Safe Spawn** — si la cellule est dans la bande haut-centre (`safeSpawnWidth` × `safeSpawnHeight`) → `null` (vide).
3. **Cavité** — si `PerlinNoise((x+cavitySeed)*cavityScale, (y+cavitySeed)*cavityScale) > cavityThreshold` → `null` (poche d'air).
4. **Pierre** — si `PerlinNoise((x+noiseSeed)*noiseScale, (y+noiseSeed)*noiseScale) > stoneThreshold` → `stoneTile`.
5. **Terre** par défaut → `dirtTile`.

Conséquence : Bedrock et Safe Spawn l'emportent toujours sur les cavités. Les cavités l'emportent sur la pierre → une poche d'air peut traverser un patch de pierre.

## Paramètres Inspector

### Tiles

| Champ | Type | Valeur initiale | Rôle |
|---|---|---|---|
| `dirtTile` | `TileBase` | *(à assigner)* | Tuile commune (HP = 1 en principe). |
| `stoneTile` | `TileBase` | *(à assigner)* | Tuile plus résistante, placée en patches. |
| `bedrockTile` | `TileBase` | *(à assigner)* | Tuile incassable sur la bordure. |

### Zone

| Champ | Valeur initiale | Rôle |
|---|---|---|
| `bottomLeft` | `(-20, -62)` | Coin inférieur gauche inclus. |
| `topRight` | `(20, -3)` | Coin supérieur droit inclus. Zone totale ≈ 41 × 60 cellules. |

### Stone Noise (patches de pierre — **inchangé**)

| Champ | Valeur | Effet si on monte / baisse |
|---|---|---|
| `stoneThreshold` | **0.6** | ↑ moins de pierre / ↓ plus de pierre. |
| `noiseScale` | **0.15** | ↑ patches plus petits et dispersés / ↓ patches plus larges. |
| `noiseSeed` | **0** | Change le motif sans toucher le reste. |

### Cavity Noise (poches d'air — **nouveau**)

| Champ | Valeur | Effet si on monte / baisse |
|---|---|---|
| `cavityThreshold` | **0.58** | ↑ moins de cavités (niveau plus massif) / ↓ plus de cavités (niveau plus ouvert). |
| `cavityScale` | **0.12** | ↑ petites poches dispersées / ↓ grandes poches connectées. |
| `cavitySeed` | **1337** | Seed indépendante de la pierre. Change le motif des cavités seul. |

**Fourchettes utiles**

- `cavityThreshold` : `[0.50, 0.65]`. Sous 0.50 le niveau devient trop vide (pas assez à creuser). Au-dessus de 0.65 les cavités deviennent rares et minuscules.
- `cavityScale` : `[0.06, 0.18]`. Plus bas = cavités type "grottes" larges. Plus haut = "fromage" avec plein de petits trous.
- Garder `cavitySeed ≠ noiseSeed` pour que les deux couches ne se superposent pas parfaitement.

### Safe Spawn

| Champ | Valeur | Rôle |
|---|---|---|
| `safeSpawnWidth` | **7** cellules | Largeur de la bande vide, centrée sur `(bottomLeft.x + topRight.x) / 2`. |
| `safeSpawnHeight` | **5** cellules | Hauteur de la bande vide, alignée sur `topRight.y`. |

Si tu mets `0` ou négatif à l'un des deux → zone désactivée, le joueur peut spawn dans un bloc. Ne pas réduire en dessous de la taille du joueur + un peu d'air au-dessus.

### Behavior

| Champ | Valeur | Rôle |
|---|---|---|
| `generateOnStart` | **true** | Si tu peins ta Tilemap à la main, décoche et appelle `Generate()` manuellement. |

## Interactions avec le reste du projet

- **Pierre** : la logique du premier Perlin est **identique à la version pré-cavités**. Aucun gameplay pierre n'est touché.
- **LightOrb** : la chance de spawn au minage est gérée par `DestructibleTerrain`, pas par ce script. Non modifié.
- **LavaHazard** : gère son propre nettoyage au fond de la map. Ce script se contente de ne jamais poser de Bedrock ailleurs qu'en bordure définie.
- **BFS de minage (4-connectivité)** : les cavités créent des zones disconnectées à la génération → le BFS reste cohérent, chaque poche est juste une composante vide de plus.

## Comment itérer rapidement

1. En Play Mode, modifier les valeurs dans l'Inspector du GameObject qui porte `TerrainGenerator`.
2. Appeler `Generate()` via un bouton debug (ou relancer la scène) pour re-sculpter.
3. Si l'apparence est bonne mais le gameplay trop ouvert → monter `cavityThreshold` de `+0.02`.
4. Si les poches sont trop petites pour y tenir debout → baisser `cavityScale` à `0.08–0.10`.
5. Pour un run différent sans tout retoucher → changer uniquement `cavitySeed`.

## Fallback de sécurité

- `dirtTile` non assignée → warning + no-op (aucune Tilemap n'est touchée).
- Zone invalide (`bottomLeft > topRight`) → warning + no-op.
- `bedrockTile` non assignée → la bordure devient de la Terre destructible (pas une crash, mais le joueur peut sortir de la map).
