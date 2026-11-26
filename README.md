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

## Limites connues

* L'état des parties est en mémoire : un redémarrage du serveur les perd.
* Une seule instance (min 1 / max 1). Passer à plusieurs demanderait Azure SignalR Service et un état partagé.
* L'URL est publique et sans protection : quiconque la connaît peut voir `/ecran` et rejoindre une partie.
* Les parties terminées sont purgées après 10 minutes, celles inactives après 6 heures.
