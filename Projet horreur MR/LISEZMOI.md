# Prêt ou pas, ils arrivent — horreur en réalité mixte (Meta Quest 3)

Une peluche géante (Alien 3, style horror plush, ~1,95 m) rôde dans ta vraie pièce.
Survis au minuteur en te cachant derrière les meubles, puis trouve le pistolet et abats-la.

## Déroulé d'une partie

1. **Scan** : le jeu lit la pièce scannée par le Quest (sol, murs, tables, maillage 3D).
   Si rien n'est scanné, il ouvre la *Configuration de l'espace* du casque.
2. **Menu** : avertissement + gâchette pour commencer.
3. **Les lumières s'éteignent** : la pièce réelle devient presque noire, la lampe torche s'allume (main gauche).
4. **Survie (8 min 40 s)** : la peluche erre (debout ou accroupie) en jouant sa boîte à musique.
   Si elle te voit, elle hurle et te poursuit. Les vrais meubles bloquent sa vue : accroupis-toi derrière.
   Elle te frappe si elle est à portée. L'écran devient de plus en plus rouge. **3 coups = mort** (jumpscare).
5. **Trouve l'arme** : un pistolet apparaît sur une table (faisceau lumineux, bip 3D, flèche au sol).
6. **Tue-la** : 5 balles. Elle s'effondre comme une peluche vide, la pièce se rallume.
7. **A / X** : rejouer.

## Contrôles (manettes)

| Action | Bouton |
|---|---|
| Commencer / tirer | Gâchette droite (ou celle de la main qui tient l'arme) |
| Allumer / éteindre la lampe | Gâchette gauche |
| Attraper le pistolet | Grip ou gâchette, main à côté de l'arme |
| Rejouer | A ou X |
| **Test** : passer le minuteur | Maintenir les deux grips + A et X (ou touche **K** dans l'éditeur) |

Lampe éteinte = la peluche te voit moins loin. Accroupi = encore moins.

## Tester sans casque (dans l'éditeur)

Ouvrir `Assets/HorrorMR/Scenes/HorrorGame.unity` puis **Play** : la simulation XR d'AR Foundation fournit une fausse pièce.
Dans la vue Game : clic droit maintenu + souris pour regarder, **ZQSD/WASD** pour marcher.
Sans manette, les mains suivent la caméra : **Espace / clic gauche** = gâchette droite (commencer, tirer),
**F** = lampe, **G** = attraper le pistolet (s'en approcher à moins de 30 cm), **Entrée** = rejouer, **K** = passer le minuteur.

Contrôles automatiques :
- *Window → General → Test Runner → PlayMode* → `HorrorSmokeTests` joue une manche gagnée et une manche perdue (sans casque).
- Menu **Horreur MR → Aperçu des poses du monstre** : planche PNG des animations (repos, marche, accroupi, poursuite, attaque, cri, mort, jumpscare).

## Installer et compiler pour le Quest 3

1. **Module Android** : Unity Hub → *Installs* → `6000.6.5f1` → ⚙ → *Add modules* →
   cocher **Android Build Support** avec **OpenJDK** et **Android SDK & NDK Tools** → *Install*.
2. Ouvrir le projet. Si la scène n'existe pas encore : menu **Horreur MR → Tout configurer (assets + scène)**.
   Ça régénère les matériaux, les prefabs, `Assets/HorrorMR/HorrorAssets.asset` et la scène `Assets/HorrorMR/Scenes/HorrorGame.unity`.
3. *File → Build Profiles* → **Android** → *Switch Platform*.
4. Casque en **mode développeur** (application Meta Horizon sur téléphone), branché en USB-C, autoriser le débogage USB dans le casque.
5. *Build Profiles → Android → Run Device* : choisir le Quest 3 → **Build And Run**.
6. Dans la salle : *Paramètres → Environnement physique → Configuration de l'espace* pour scanner la pièce et ses tables.
   Au premier lancement, accepter la permission « données spatiales ».

## Organisation du code (`Assets/HorrorMR`)

| Fichier | Rôle |
|---|---|
| `Scripts/GameManager.cs` | Déroulé de la partie (scan, menu, survie, arme, victoire, mort, rejouer) |
| `Scripts/RoomModel.cs` | Lit la pièce (plans, meubles, maillage), construit le NavMesh, trouve les points d'apparition et la table du pistolet |
| `Scripts/MonsterAI.cs` | IA : patrouille, vue (cône + lancer de rayons sur la vraie pièce), poursuite, attaque, recherche, mort, jumpscare |
| `Scripts/PlushAnimator.cs` | Animations procédurales du squelette Alien3 (marche dandinante, accroupi, course bras tendus, coups, mort) |
| `Scripts/Gun.cs`, `Flashlight.cs` | Pistolet (snap dans la main, tir, aide à la visée) et lampe torche |
| `Scripts/DarknessController.cs` + `Shaders/` | Obscurcit le passthrough sauf dans le faisceau de la lampe, occlusion par les meubles réels |
| `Scripts/ScreenFX.cs`, `HUD.cs` | Écran rouge / vignette / fondu, textes flottants et flèche |
| `Editor/HorrorSetup.cs` | Génère tout le contenu (menu *Horreur MR*) |

Réglages utiles dans l'inspecteur : `GameManager.survivalSeconds` (durée de survie),
`MonsterAI` (vitesses, portée de vue, vie = 5), `DarknessController.maxDarkness` (0,9 : ne pas mettre 1 pour toujours voir les obstacles réels).

## Notes

- Les sons sont **synthétisés par script** (libres de droits). On peut les remplacer par des fichiers du même nom dans `Assets/HorrorMR/Audio`.
- La texture « salie + sang » de la peluche est générée à partir de `Alien3_Albedo.png` (`Assets/HorrorMR/Textures/Alien3_Horror_Albedo.png`).
- Le pistolet et la lampe sont faits de formes simples : il suffit de remplacer leurs prefabs par de vrais modèles.
- Sécurité : la pièce n'est jamais totalement noire. Jouer dans une zone dégagée.
