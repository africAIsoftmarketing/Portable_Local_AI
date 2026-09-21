"""
Rôle    : squelette minimal pour créer un nouveau skill MCP.
          Copiez ce dossier vers mcp-servers/<votre-skill>/ et adaptez.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import sys
from pathlib import Path

_HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(_HERE.parent))
from _shared.mcp_server import MCPServer  # noqa: E402

server = MCPServer(
    name="template",
    version="1.0.0",
    description_fr="Skill exemple - remplacez cette description.",
    description_en="Example skill - replace this description.",
)


@server.tool(
    name="echo",
    description="Renvoie l'argument tel quel (démonstration).",
    input_schema={
        "type": "object",
        "properties": {"message": {"type": "string"}},
        "required": ["message"],
    },
)
def echo(args: dict) -> dict:
    return {"echo": args.get("message", ""),
            "length": len(args.get("message", ""))}


if __name__ == "__main__":
    server.run()
