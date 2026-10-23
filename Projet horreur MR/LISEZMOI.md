# Prêt ou pas, ils arrivent — horreur en réalité mixte (Meta Quest 3)

Une peluche géante (Alien 3, style horror plush, ~2,40 m) rôde dans ta vraie pièce.
Survis au minuteur en te cachant derrière les meubles, puis trouve le pistolet et abats-la.

## Déroulé d'une partie

1. **Pièce** : le jeu recharge la pièce enregistrée (ancre spatiale du Quest). Sinon il lance le **scan manuel** (voir plus bas).
   Le Guardian est masqué automatiquement pour pouvoir circuler dans toute la salle.
2. **Menu** : avertissement + gâchette pour commencer (B / Y : refaire le scan).
3. **Les lumières s'éteignent** : la pièce réelle devient presque noire, la lampe torche s'allume (main gauche).
4. **Survie (1 min 30 s)** : la peluche erre (debout ou accroupie) en jouant sa boîte à musique.
   Si elle te voit, elle hurle et te poursuit. Les vrais meubles bloquent sa vue : accroupis-toi derrière.
   Elle te frappe au corps à corps. L'écran devient de plus en plus rouge. **3 coups = mort** (jumpscare).
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

## Scan manuel de la pièce

Le scan du Quest ne couvre qu'une partie d'une grande salle (et la version actuelle du casque ne transmet pas ses surfaces au jeu) :
on mesure donc la pièce à la main, en **posant la manette** sur les coins (plus précis que viser).

1. **Pièce (3 points)** : manette au sol dans un coin (**coin 1**, à retenir) → gâchette ; au sol dans le **coin 2**, au bout du même mur → gâchette ;
   au sol dans un coin du **mur d'en face** → gâchette.
2. **Meubles (2 points chacun)** : manette au sol contre un coin du meuble → gâchette ; puis **sur le dessus**, au coin opposé en diagonale → gâchette.
   Une rangée de tables collées peut être saisie comme un seul meuble. Les boîtes s'affichent en orange.
3. **A / X** : terminer. **B / Y** : annuler le dernier point ou le dernier meuble.

La pièce est enregistrée dans le casque avec une ancre spatiale et rechargée au lancement suivant.
Si le casque ne la retrouve pas : gâchette = recaler en touchant les coins 1 et 2, ou B / Y = tout rescanner.
Pour refaire le scan à tout moment : **B / Y** au menu.

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
   Ou bien : menu **Horreur MR → Compiler l'APK Quest** (crée `Builds/PretOuPas.apk`), puis l'installer avec
   `adb install -r Builds/PretOuPas.apk` (adb est dans le module Android de Unity).
6. Au premier lancement, accepter la permission « données spatiales », puis faire le scan manuel de la salle.

## Organisation du code (`Assets/HorrorMR`)

| Fichier | Rôle |
|---|---|
| `Scripts/GameManager.cs` | Déroulé de la partie (scan, menu, survie, arme, victoire, mort, rejouer) |
| `Scripts/RoomModel.cs` | Lit la pièce (plans, meubles, maillage), construit le NavMesh, trouve les points d'apparition et la table du pistolet |
| `Scripts/RoomCalibrator.cs`, `ManualRoom.cs`, `RoomLayout.cs` | Scan manuel à la manette, pièce virtuelle (sol, murs, meubles), sauvegarde + ancre spatiale |
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
