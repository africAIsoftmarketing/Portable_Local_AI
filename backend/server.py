"""
Rôle    : shim de démarrage pour l'environnement de développement Emergent.
          Supervisor lance `uvicorn server:app --host 0.0.0.0 --port 8001`
          depuis /app/backend/. Ce fichier re-exporte l'application
          principale située dans /app/app/main.py.
          En mode portable production, ce fichier n'est PAS utilisé :
          start-linux.sh/.bat/.command lancent directement `uvicorn app.main:app`.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
import os
import sys
from pathlib import Path

# Ajoute la racine du dépôt au PYTHONPATH pour permettre `import app`.
REPO_ROOT = Path(__file__).resolve().parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

# En mode dev, toutes les routes sont préfixées par /api (ingress Kubernetes
# route /api/* vers ce process sur le port 8001). En portable, préfixe vide.
os.environ.setdefault("STUDIO_API_PREFIX", "/api")

# En dev, ne pas booter llama-server automatiquement si les binaires
# ne sont pas disponibles pour la plateforme (test manuel via /api/health).
# La variable est lue par app/main.py au démarrage.
# Si un binaire arm64 CPU est présent, on tente de booter llama-server ;
# sinon on démarre en mode "chat-desactive" mais l'API répond quand même.

from app.main import app  # noqa: F401,E402
