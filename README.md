---
title: Life Counter Assist
description: Compteur de points de vie partagé pour Magic The Gathering (Commander) en salle de réunion, avec un écran de salle, sans compte ni installation
ms.date: 2026-11-26
ms.topic: overview
---

## Présentation

Life Counter Assist sert à jouer à Magic (format Commander) entre collègues dans une salle de réunion. Le PC de la salle affiche un QR code sur `/ecran`. Chacun le scanne avec son téléphone, tape un pseudo et rejoint la partie. Aucun compte, aucune installation.

* Accueil : liste des parties en cours, création d'une partie avec un seul choix (PV de départ : 20, 25, 30 ou 40)
* Lobby en direct, le créateur démarre la partie (2 à 8 joueurs)
* Sur chaque téléphone : PV (tap ±1, appui long ±10), poison, dégâts de commandant par adversaire
* Sur l'écran de la salle : QR code au repos, lobby, puis un tableau des PV de tous les joueurs en grand
* Fin automatique avec compte à rebours de 10 s quand il ne reste qu'un joueur
* Reprise de sa place en rejoignant avec le même pseudo

## Architecture

```text
Téléphones (React)    ──REST + SignalR──►  App .NET 10 (1 conteneur, 1 instance)
PC de la salle /ecran ──SignalR (lecture)─┘   sert aussi le front (wwwroot)
                                              état des parties en mémoire
```

| Dossier              | Contenu                                                                  |
|----------------------|--------------------------------------------------------------------------|
| `backend/`           | API ASP.NET Core (REST, hub SignalR `/hubs/game`, `/health`), tests xUnit |
| `frontend/`          | React 19, Vite, Tailwind CSS, i18next, client SignalR                    |
| `Dockerfile`         | Image unique : build du front, publication de l'API, front dans `wwwroot` |
| `infra/bicep/`       | Azure : Container Apps (une app), ACR, Log Analytics, identité managée   |
| `docker-compose.yml` | Lance l'image unique en local                                            |

L'API sert le front compilé : même origine, pas de CORS ni de configuration côté client. Les routes `/api/...` inconnues renvoient 404.

## Développement local

Prérequis : .NET SDK 10 et Node.js 22 ou plus.

```powershell
# Terminal 1 : API sur http://localhost:5075
cd backend\src\LifeCounterAssist.Api
dotnet run --launch-profile http

# Terminal 2 : front sur http://localhost:5180 (proxy vers l'API)
cd frontend
npm install
npm run dev
```

Pour tester à plusieurs, ouvre l'app dans deux navigateurs (ou une fenêtre privée) avec deux pseudos. La page de la salle est sur <http://localhost:5180/ecran>.

Tests et build :

```powershell
cd backend; dotnet test
cd ..\frontend; npm run build
```

## Docker

```powershell
docker compose up --build
```

L'app est servie sur <http://localhost:8080> (écran de salle sur `/ecran`, santé sur `/health`).

## Déploiement sur Azure

L'infrastructure se déploie en deux passes : la première crée le registre et l'identité, la seconde crée la Container App une fois l'image poussée.

```powershell
$rg = "rg-lca-prod-001"
az group create --name $rg --location francecentral

# 1. Socle (imageTag laissé à null)
az deployment group create --resource-group $rg --template-file infra/bicep/main.bicep --parameters infra/bicep/main.bicepparam
$acr = az deployment group show --resource-group $rg --name main --query properties.outputs.registryName.value -o tsv

# 2. Image, construite dans ACR (pas besoin de Docker en local)
$tag = "1.0.0"
az acr build --registry $acr --image "lca-app:$tag" .

# 3. Application
az deployment group create --resource-group $rg --template-file infra/bicep/main.bicep --parameters infra/bicep/main.bicepparam --parameters imageTag=$tag
```

La sortie `appUrl` donne l'URL publique : c'est elle qu'encode le QR code (ouvre `<appUrl>/ecran` sur le PC de la salle). Pour une mise à jour, reconstruis l'image avec un nouveau tag puis relance l'étape 3.

## Déploiement sur Raspberry Pi

Le workflow **Deploy to Raspberry Pi** (`.github/workflows/deploy-pi.yml`) construit l'image ARM64 sur GitHub, la publie sur `ghcr.io/wanteex/lifecounterassist`, puis la déploie sur la Pi à travers Tailscale. Il se lance uniquement à la main (onglet Actions, bouton **Run workflow**) : un déploiement redémarre l'app et coupe les parties en cours, gardées en mémoire.

```text
Run workflow ─► runner ARM GitHub : build + push ghcr.io
             └► runner Ubuntu : rejoint le tailnet (tag:ci) ─ssh─► Pi : docker compose pull && up -d
```

### Mise en place (une seule fois)

Sur la Pi (Raspberry Pi OS 64 bits, Docker installé, SSH actif) :

```bash
# Utilisateur de déploiement dans le groupe docker
sudo adduser --disabled-password --gecos "" deploy
sudo usermod -aG docker deploy

# Clé SSH dédiée au workflow (la partie privée ira dans GitHub)
ssh-keygen -t ed25519 -N "" -C "github-deploy" -f ./deploy_key
sudo install -d -m 700 -o deploy -g deploy /home/deploy/.ssh
cat ./deploy_key.pub | sudo tee -a /home/deploy/.ssh/authorized_keys
sudo chown deploy:deploy /home/deploy/.ssh/authorized_keys && sudo chmod 600 /home/deploy/.ssh/authorized_keys

# URL publique HTTPS vers l'app (écoute sur 127.0.0.1:8080)
sudo tailscale funnel --bg 8080
```

Dans la console Tailscale :

1. Politique d'accès : déclarer le tag et autoriser SSH vers la Pi.

   ```json
   "tagOwners": { "tag:ci": ["autogroup:admin"] },
   "grants": [{ "src": ["tag:ci"], "dst": ["<nom-de-la-pi>"], "ip": ["22"] }]
   ```

2. Settings > OAuth clients : créer un client avec l'écriture sur **Auth Keys** et le tag `tag:ci`.

Dans GitHub, Settings > Secrets and variables > Actions, créer :

| Secret                | Valeur                                                    |
|-----------------------|-----------------------------------------------------------|
| `TS_OAUTH_CLIENT_ID`  | ID du client OAuth Tailscale                              |
| `TS_OAUTH_SECRET`     | Secret du client OAuth Tailscale                          |
| `PI_HOST`             | Nom MagicDNS de la Pi sur le tailnet (ex. `raspberrypi`) |
| `PI_USER`             | `deploy`                                                  |
| `PI_SSH_KEY`          | Contenu du fichier `deploy_key` (clé privée)             |

Puis supprimer `deploy_key` et `deploy_key.pub` de la Pi.

Après le premier lancement du workflow, rendre l'image publique pour que la Pi la télécharge sans identifiants : profil GitHub > Packages > `lifecounterassist` > Package settings > Change visibility > Public.

L'écran de salle est sur `https://<nom-de-la-pi>.<tailnet>.ts.net/ecran` : le QR code reprend l'adresse de la page, il pointe donc sur l'URL publique.

## Limites connues

* L'état des parties est en mémoire : un redémarrage du serveur les perd.
* Une seule instance (min 1 / max 1). Passer à plusieurs demanderait Azure SignalR Service et un état partagé.
* L'URL est publique et sans protection : quiconque la connaît peut voir `/ecran` et rejoindre une partie.
* Les parties terminées sont purgées après 10 minutes, celles inactives après 6 heures.
