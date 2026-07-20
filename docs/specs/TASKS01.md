<!-- task-status: done; date: 2026-07-20 -->
# SmartIndexManager

## Traitement

Brainstorm mené le 2026-07-20 : contradictions résolues, trous fonctionnels comblés, 12 décisions supplémentaires actées avec l'utilisateur. Le tout est consolidé dans `2026-07-20-smartindexmanager-design.md`, qui devient la spécification de référence.

Note : ce document est la trace historique du brainstorm initial. La spécification consolidée et à jour est dans `2026-07-20-smartindexmanager-design.md`.

## Objectif

Un outil de gestion des index dans SQL Server en premier lieu, et pour le futur : PostgreSQL et potentiellement sur d'autres moteurs de base de données.

## Architecture

- Un moteur agnostique avec un certain nombre de fonctionnalités sur les index.
- Des providers pour les différents moteurs de base de données.

## Fonctionnalités

- Lister tous les index sur toutes les tables avec leurs clés et leurs structures.
- Identifier les index utilisés et non utilisés en utilisant les outils de diagnostic.
- Détection algorithmique des index redondants, c'est-à-dire ceux qui sont placés sur le même début de clé.
- Recherche dans le cache de plan d'exécution des requêtes qui utilisent un index donné.
- Suppression des index inutilisés avec :
  - conservation du DDL dans un fichier SQL pour recréation de l'index en cas d'erreur ;
  - stockage dans des fichiers SQL des informations de suppression et de la raison de la suppression ;
  - stockage dans un répertoire SmartIndexManager des documents de l'utilisateur, avec un sous-répertoire daté.

## Technologie

- C#
- .NET 10
- Avalonia (multi-plateforme)
- Répertoire `sql` avec les requêtes utilisées par SGBD.
  - Requêtes pour SQL Server récupérées sur : https://github.com/rudi-bruchez/tsql-scripts/tree/main/index-management

## Analyse de faisabilité

Le projet est globalement faisable. Les scripts SQL Server de référence couvrent déjà l'essentiel des besoins listés (`index-usage.sql`, `unused-indexes.sql`, `index-used-by-queries.sql`, `index-used-by-queries-query-store.sql`, `missing-indexes.sql`).

### Points solides

- Les fonctionnalités de listing, diagnostic d'usage et recherche dans le plan cache reposent sur des DMV SQL Server standards.
- La pile C# / .NET 10 / Avalonia est cohérente et mature. `Microsoft.Data.SqlClient` couvre la connexion depuis Windows, Linux et macOS.
- L'architecture moteur agnostique + providers par SGBD est le bon pattern (interface `IDatabaseProvider` avec méthodes de métadonnées et de diagnostic).

### Points de vigilance

- Les DMV d'usage sont volatiles : `sys.dm_db_index_usage_stats` est remise à zéro à chaque redémarrage de l'instance (et à la mise offline de la base). Un index jugé inutilisé peut servir à une fréquence mensuelle ou trimestrielle (batch de clôture). --> ce sera à juger par l'utilisateur. On peut prévoir la mise en place à traver l'outil d'un système de récupération des informztions dans des tables dédiées, avant SHUTDOWN. A brainstormer pour v2
- Certains index « inutilisés » sont critiques : contraintes `UNIQUE`, index supportant des clés étrangères, index filtrés, index hypothétiques. --> à lister spécifiquement dans l'interface avec garde-fou et filtre.
- Le plan cache est volatile aussi. Le Query Store (SQL Server 2016+) est plus fiable, mais il n'est pas activé partout. -- à mentionner dans l'interface, et prévoir activation du Query Store.
- La détection de redondance est plus subtile que « même début de clé » : il faut comparer la clé, les colonnes `INCLUDE`, le prédicat de filtre et l'unicité.
- Les droits requis sont élevés : `VIEW SERVER STATE` pour les DMV, et droits DDL pour le `DROP INDEX`. L'utilisateur cible est un DBA, pas un applicatif. -- OUI, et vérifier les permissions à la connexion

## Questions en suspens

### Périmètre fonctionnel

1. La suppression est-elle la seule action corrective, ou faut-il aussi prévoir le rebuild, la réorganisation et la création d'index manquants ? -- il faut tout prévoir. Mais déjà suppression pour le MVP
2. Gestion multi-bases : une base à la fois, toutes les bases d'une instance, plusieurs instances simultanément ? -- une base ou plusieurs bases d'une instance, sélection multiple
3. Faut-il un gestionnaire de connexions sauvegardées (plusieurs serveurs, authentification SQL, Windows, Entra ID) ? -- OUI
4. Faut-il un mode simulation (dry-run) avant suppression, avec rapport d'impact ? -- OUI

### Sécurité de la suppression

5. Quels seuils qualifient un index de supprimable (zéro seek/scan, critères sur les updates, uptime minimum de l'instance) ? -- 0 seeks : afficher les seek, scan, updates dans l'interface, et colorer de vert à orange rouge selon la confiance de suppression possible
6. Quelles exclusions automatiques sont requises (PK, contraintes UNIQUE, index de clés étrangères, tables système) ? -- toutes celles-là. Ne pas prendre en compte les index clustered pour suppression
7. Faut-il une étape intermédiaire de désactivation (`ALTER INDEX ... DISABLE`) avant le `DROP`, plus facilement réversible à chaud ? -- NON
8. Le répertoire `Documents/SmartIndexManager/<date>` convient-il aussi sur Linux/macOS, ou faut-il un chemin configurable ? -- Configurable
9. Format des fichiers de sauvegarde : un fichier SQL par index, ou un fichier global plus un manifeste (JSON) avec date, serveur, base, raison, opérateur ? -- un fichier par index
10. Faut-il une fonction de restauration intégrée (rejouer le DDL sauvegardé depuis l'outil) ? -- oui, bonne idée

### Technique

11. Quelles versions minimales de SQL Server sont supportées ? Les DMV et le Query Store varient selon les versions. Azure SQL Database est-il inclus ? -- 2012, Azure SQL inclus
12. Comment modéliser un index de façon agnostique entre SQL Server et PostgreSQL (clustered, include, fillfactor n'ont pas d'équivalent direct) ? -- prévoir un module par SGBD
13. Les scripts SQL sont-ils embarqués en ressources ou conservés comme fichiers externes modifiables par l'utilisateur ? -- fichiers externes
14. L'application est-elle purement interactive, ou faut-il aussi un mode CLI pour l'automatisation ? -- interactive
15. Comment tester contre un vrai SQL Server en CI (conteneur Docker, bac à sable partagé) ? -- je m'en occupe
