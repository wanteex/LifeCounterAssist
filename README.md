---
title: Life Counter Assist
description: Compteur de points de vie partagé en temps réel pour Magic The Gathering (Commander), en PWA mobile avec une API .NET et SignalR
ms.date: 2026-05-07
ms.topic: overview
---

## Présentation

Life Counter Assist est une web app mobile (PWA) qui sert de compteur de points de vie partagé pour Magic The Gathering, pensée d'abord pour le format Commander. Chaque joueur utilise son propre téléphone et voit le même plateau, synchronisé en temps réel.

Fonctionnalités du MVP :

* Création d'une partie (2 à 8 joueurs, 20/25/30/40 PV) avec invitation d'amis ou code à 5 caractères
* Plateau partagé : PV, dégâts de commandant par source, poison, taxe de commandant, énergie, expérience, monarque, initiative, abandon
* Tap pour ±1, appui long pour ±10, mise à jour optimiste et journal de partie
* Profil avec statistiques et historique des parties
* Decks ajoutés par lien (Archidekt importé, Moxfield en best effort) ou par nom de commandant via Scryfall, avec l'illustration du commandant en fond de tuile
* Amis via un code ami, avec demandes et acceptation en direct
* Interface en français et en anglais
* Installation sur l'écran d'accueil, écran maintenu allumé pendant une partie

## Architecture

```text
Téléphone (PWA React)  ──HTTPS/REST──►  API .NET 10 (Container App, 1 réplique)
        ▲                                   │
        └──────── SignalR (WebSocket) ◄─────┤
                                            ▼
                                Azure Table Storage (identité managée)
Connexion : Microsoft Entra External ID (Google)
```

| Dossier              | Contenu                                                                     |
|----------------------|-----------------------------------------------------------------------------|
| `backend/`           | API ASP.NET Core (minimal API + hub SignalR `/hubs/game`), tests xUnit      |
| `frontend/`          | React 19, Vite, Tailwind CSS, i18next, MSAL, client SignalR, service worker |
| `infra/bicep/`       | Infrastructure Azure (Container Apps, ACR, Storage, Log Analytics)          |
| `docker-compose.yml` | Stack locale : Azurite, API, web                                            |

L'API garde l'état des parties en cours en mémoire, avec un verrou par partie, et persiste chaque modification dans Table Storage. Elle doit donc tourner sur une seule réplique.

## Développement local

Prérequis : .NET SDK 10, Node.js 22 ou plus, et Azurite (via Docker ou `npx azurite`).

En local, l'authentification passe en mode Dev : tu saisis un pseudo et l'API fait confiance au jeton `dev:{id}:{nom}`. Ce mode est refusé hors de l'environnement `Development`.

### Sans Docker

```powershell
# Terminal 1 : émulateur de stockage
npx azurite@latest --silent --location $env:TEMP\lca-azurite

# Terminal 2 : API sur http://localhost:5075
cd backend\src\LifeCounterAssist.Api
dotnet run --launch-profile http

# Terminal 3 : front sur http://localhost:5180
cd frontend
npm install
npm run dev
```

Pour tester à plusieurs, ouvre l'app dans deux navigateurs (ou une fenêtre privée) avec deux pseudos différents. Depuis un téléphone sur le même réseau, remplace `localhost` par l'IP du poste dans `frontend/public/config.js` et dans `Cors:AllowedOrigins` de `appsettings.Development.json`.

### Avec Docker Compose

```powershell
docker compose up --build
```

L'app est servie sur <http://localhost:8080> et l'API sur <http://localhost:5075>.

### Tests et build

```powershell
cd backend; dotnet test
cd ..\frontend; npm run build
```

## Configuration

### API

| Variable                           | Rôle                                                           |
|------------------------------------|----------------------------------------------------------------|
| `Auth__Mode`                       | `Entra` (défaut) ou `Dev` (uniquement en Development)          |
| `Auth__Authority`                  | Autorité des jetons (voir la section Entra External ID)        |
| `Auth__Audience`                   | Audiences acceptées, séparées par des virgules                 |
| `Storage__ConnectionString`        | Chaîne de connexion (Azurite)                                  |
| `Storage__TableEndpoint`           | Endpoint Table, utilisé avec l'identité managée                |
| `Storage__ManagedIdentityClientId` | Client id de l'identité managée user-assigned                  |
| `Cors__AllowedOrigins__0`          | Origine du front                                               |

### Front

Le conteneur nginx génère `config.js` au démarrage. La même image sert donc pour tous les environnements.

| Variable          | Rôle                                                |
|-------------------|-----------------------------------------------------|
| `API_URL`         | URL publique de l'API                               |
| `AUTH_MODE`       | `entra` ou `dev`                                    |
| `ENTRA_CLIENT_ID` | Client id de l'application SPA                      |
| `ENTRA_AUTHORITY` | `https://<sous-domaine>.ciamlogin.com/`             |
| `ENTRA_API_SCOPE` | `api://<api-client-id>/access_as_user`              |

## Mise en place de Microsoft Entra External ID

1. Crée un tenant externe dans le centre d'administration Microsoft Entra (External Identities). Note son sous-domaine et son tenant id.
2. Enregistre l'application API (`lca-api`) :
   * Expose an API : App ID URI `api://<api-client-id>` et scope délégué `access_as_user`
   * Dans le manifeste, `requestedAccessTokenVersion` doit valoir `2`
3. Enregistre l'application SPA (`lca-spa`) :
   * Plateforme Single-page application, URI de redirection `https://<url-du-web>` et `http://localhost:5180`
   * API permissions : `lca-api` / `access_as_user`, puis accorde le consentement administrateur
4. Configure Google comme fournisseur d'identité :
   * Dans Google Cloud Console, crée un client OAuth « Web application » avec les URI de redirection `https://<tenant-id>.ciamlogin.com/<tenant-id>/federation/oauth2` et `https://<sous-domaine>.ciamlogin.com/<tenant-id>/federation/oauth2`
   * Dans External Identities, ajoute Google avec ce client id et ce secret
5. Crée un user flow « Inscription et connexion » avec Google (et l'e-mail si tu veux), collecte l'attribut Display Name, puis associe-lui l'application `lca-spa`.

Les valeurs obtenues alimentent le paramètre `auth` de `infra/bicep/main.bicepparam`.

> [!NOTE]
> Si le jeton d'accès ne contient pas de claim `name`, l'API utilise la partie locale de l'e-mail, ou « Player ». Chaque joueur peut changer son nom depuis son profil.

## Déploiement sur Azure

L'infrastructure se déploie en deux passes : la première crée le registre, le stockage et l'identité, la seconde crée les Container Apps une fois les images poussées.

```powershell
$rg = "rg-lca-prod-001"
az group create --name $rg --location francecentral

# 1. Socle (imageTag laissé à null)
az deployment group create --resource-group $rg --template-file infra/bicep/main.bicep --parameters infra/bicep/main.bicepparam
$acr = az deployment group show --resource-group $rg --name main --query properties.outputs.registryName.value -o tsv

# 2. Images, construites dans ACR (pas besoin de Docker en local)
$tag = "1.0.0"
az acr build --registry $acr --image "lca-api:$tag" backend
az acr build --registry $acr --image "lca-web:$tag" frontend

# 3. Applications
az deployment group create --resource-group $rg --template-file infra/bicep/main.bicep --parameters infra/bicep/main.bicepparam --parameters imageTag=$tag
```

La sortie `webUrl` donne l'URL de l'app : ajoute-la comme URI de redirection de l'application SPA. Pour une mise à jour, reconstruis les images avec un nouveau tag puis relance l'étape 3.

## Limites connues

* L'API doit rester sur une seule réplique (état en mémoire, verrou par partie). Passer à plusieurs instances demanderait Azure SignalR Service et un verrou distribué.
* L'import Moxfield dépend d'une API non officielle et peut échouer. Le deck est alors créé avec le nom et le commandant saisis (illustration et couleurs via Scryfall).
* Le journal d'une partie garde les 100 dernières entrées.
* Le Blob Storage n'est pas encore utilisé (piste : avatars).
