# Nexio pour Windows

Le compagnon PC de **[Nexio](https://github.com/gaara-ux/nexio)** : contrôle ta TV Android / Google TV
au clavier et à la souris, et échange des fichiers et des applis avec ton téléphone et ta TV.

Un seul fichier, rien à installer, aucune dépendance (le .NET Framework 4.8 est déjà dans Windows 10 et 11).

## Ce qu'elle fait

- **Télécommande** : pavé directionnel, boutons, volume, et écriture sur la TV. Raccourcis : flèches, Entrée = OK,
  Échap = Retour, Début = Accueil, Espace = lecture/pause, + / − = volume, M = muet.
- **Fichiers** : deux panneaux (PC et téléphone ou TV), glisser-déposer depuis l'Explorateur, copier, déplacer,
  renommer, supprimer. Double-clic sur un APK pour l'installer sur un appareil.
- **Applis** : récupère les APK d'un téléphone ou d'une TV sur le PC, ou envoie-les vers un autre appareil.
- **Écran** : voir et contrôler le téléphone ou la TV avec [scrcpy](https://github.com/Genymobile/scrcpy)
  (téléchargé au premier usage). Une barre de télécommande se colle à côté de la fenêtre : déplace-la,
  réduis-la ou ferme-la.
- **Système** : vérifie le pare-feu et le réseau, et répare tout en un clic.

## Installer

1. Télécharge `Nexio.exe` dans la page **[Releases](https://github.com/gaara-ux/nexio/releases/latest)**.
2. Lance-le. Windows peut afficher « Windows a protégé votre ordinateur » parce que le fichier n'est pas signé
   par un éditeur payant : clique **Informations complémentaires**, puis **Exécuter quand même**.
3. Va dans **Système** et clique **Tout réparer** (une seule autorisation administrateur) : le pare-feu laisse
   alors le téléphone et la TV voir le PC.
4. Appaire : dans l'appli du téléphone, onglet **Fichiers** › **+ Appareil**, tape le code affiché en haut à
   gauche du PC.

Pour l'onglet **Écran**, active le débogage réseau (TV) ou le débogage sans fil (téléphone) dans les options
pour les développeurs.

## Compiler soi-même

```
build.cmd
```

Il utilise le compilateur C# fourni avec Windows : rien à installer.

## Licence

[MIT](LICENSE)
