# 2Ship Discord Presence (`2ShipDiscordPresence.exe`)

Détecteur automatique de statut Discord Rich Presence pour **2Ship2Harkinian** (Le portage PC de *The Legend of Zelda: Majora's Mask*).

![Majora Icon](majora_icon.jpg)

---

## Fonctionnalités

- 🔍 **Détection automatique** : Surveille le processus `2ship.exe` (ou `2Ship2Harkinian.exe`).
- 🎮 **Présence Discord personnalisée** :
  - **Titre principal** : `Majora's Mask`
  - **Détails** : `The Legend of Zelda: Majora's Mask`
  - **État** : `Playing 2Ship2Harkinian`
  - **Icône** : Masque de Majora
  - **Compteur** : Chronomètre de temps de jeu en direct
- ⚙️ **Fichier `config.json`** : Modifiez à tout moment les textes, l'image ou le `client_id` sans récompiler le logiciel.
- ⚡ **Léger & Autonome** : Compilé nativement pour Windows, sans Python ni Node.js requis.

---

## Utilisation

1. Démarrez **`2ShipDiscordPresence.exe`**.
2. Lancez votre jeu **`2ship.exe`**.
3. L'application détecte automatiquement le jeu et active la Présence Discord !
4. Dès que vous quittez le jeu, le statut Discord est retiré automatiquement.

---

## Personnaliser votre propre Application Discord (Optionnel)

Pour que Discord affiche précisément **"Joue à Majora's Mask"** en gras au sommet du profil :

1. Rendez-vous sur le **[Portail Développeur Discord](https://discord.com/developers/applications)**.
2. Cliquez sur **"New Application"** et nommez-la **`Majora's Mask`**.
3. Dans la section **"General Information"**, copiez l'**Application ID**.
4. Dans le fichier `config.json`, remplacez `"client_id"` par votre ID copié :
   ```json
   {
     "client_id": "VOTRE_APPLICATION_ID_ICI",
     "process_name": "2ship",
     "details": "The Legend of Zelda: Majora's Mask",
     "state": "Playing 2Ship2Harkinian",
     "large_image": "majora",
     "large_text": "Majora's Mask",
     "check_interval_seconds": 3
   }
   ```
5. *(Optionnel)* Dans l'onglet **"Rich Presence" > "Art Assets"** sur le Portail Discord, ajoutez une image sous le nom `majora` (utilisez l'image `majora_icon.jpg` incluse).

---

## Recompilation

Si vous modifiez le code source `Program.cs`, double-cliquez simplement sur `build.bat` pour générer à nouveau `2ShipDiscordPresence.exe`.
