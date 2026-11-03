# Phase 1 — Setup du GameObject Player (côté Editor)

Les scripts et l'Input Actions asset sont en place. Comme le MCP Unity n'est
pas branché pour cette session, voici les étapes manuelles côté Editor
pour avoir un joueur fonctionnel dans la scène.

## 1. Vérifier les Layers

`Edit > Project Settings > Tags and Layers` — tu dois voir :

- Layer 6 : **Ground**
- Layer 7 : **Player**

(Déjà écrits dans `ProjectSettings/TagManager.asset` — redémarre Unity si
tu les ne vois pas.)

## 2. Créer le GameObject Player

Dans la Hierarchy :

1. `Create Empty` → le nommer **Player**.
2. Lui assigner le Layer **Player** (menu déroulant en haut de l'Inspector).
3. Ajouter les composants :
   - **Sprite Renderer** (pour visualiser — un sprite temporaire type `UISprite` ou un carré 1×2 suffit)
   - **Rigidbody2D**
     - Body Type : *Dynamic*
     - Gravity Scale : *laisser à 1* (le script la recalcule au démarrage)
     - Constraints → Freeze Rotation Z : coché *(le script le fait aussi au Awake, mais autant être propre)*
     - Collision Detection : *Continuous*
     - Interpolate : *Interpolate*
   - **Capsule Collider 2D**
     - Direction : *Vertical*
     - Size ≈ (0.8, 1.9) selon ton sprite

## 3. Créer le GroundCheck

Dans Player :

1. `Create Empty` enfant → le nommer **GroundCheck**.
2. Le positionner **juste sous les pieds** du collider :
   - Local Position Y ≈ `-( capsuleSize.y / 2 )` (ex : `-0.95` si Size.y = 1.9)
3. Il n'a besoin d'aucun composant — c'est juste un Transform pivot.

## 4. Ajouter PlayerMovement2D

Sur le GameObject Player :

1. `Add Component > PlayerMovement2D`.
2. Remplir l'inspector :
   - **Ground Check** → glisser-déposer l'enfant GroundCheck
   - **Ground Check Size** → `(0.9, 0.1)` par défaut (ajuste si capsule plus large)
   - **Ground Layer** → cocher *Ground*
   - **Input Actions** → glisser-déposer `Assets/Input/PlayerInputActions.inputactions`

Les autres valeurs (moveSpeed, jumpHeight, timeToApex, coyote, buffer…) sont
pré-réglées pour un feel de platformer "nerveux mais lisible". Tu peux
tout tweaker dans l'Inspector sans recompiler.

## 5. Préparer un sol de test

Pour tester le saut, il te faut quelque chose au Layer *Ground* :

- Option rapide : un GameObject vide avec un **BoxCollider2D** étiré horizontalement,
  Layer *Ground*.
- Pour la Phase 2 ce sera remplacé par une Tilemap avec `TilemapCollider2D`
  sur le Layer *Ground*.

## 6. Tester

Entre en Play. Tu dois pouvoir :

- Bouger ZQSD au clavier (positions physiques, fonctionne AZERTY et QWERTY)
- Sauter avec Espace
- Bouger stick gauche + saut bouton Sud (A sur Xbox / Croix sur PlayStation) à la manette

## Raisonnement physique (notes de design)

- **Gravité calculée** depuis `jumpHeight` et `timeToApex` plutôt qu'en
  tunant `gravityScale` à l'aveugle. On règle d'abord ce qu'on veut *sentir*
  (hauteur, temps), et le code en déduit la gravité et la vitesse initiale.
- **Coyote time (100 ms)** : fenêtre après avoir quitté une plateforme où
  le saut reste permis. Supprime le ressenti "j'ai appuyé mais rien".
- **Jump buffer (120 ms)** : un appui Jump juste avant l'atterrissage est
  mémorisé et consommé au touché du sol. Supprime le "ah j'ai sauté trop tôt".
- **Variable jump height** : relâcher Espace tôt coupe le saut (gravité
  amplifiée par `lowJumpMultiplier` en montée). Tap court = petit saut,
  maintien = grand saut.
- **Fall gravity multiplier > 1** : la chute est plus rapide que la montée,
  pour un ressenti plus nerveux (classique Hollow Knight / Celeste).
- **Clamp maxFallSpeed** : évite l'accélération infinie dans les longues
  chutes (important pour un jeu de minage avec puits profonds).
- **MoveTowards avec accel/decel distincts sol/air** : contrôle précis au
  sol, moins d'autorité en l'air (feel plus "lourd" quand on est en saut).
- **Ground check par OverlapBox** : plus tolérant qu'un raycast simple sur
  les arêtes de tuiles, important dès que la Phase 2 ajoutera une Tilemap.
