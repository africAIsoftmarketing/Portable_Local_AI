"""
Rôle    : skill MCP cybersec. 2 outils :
          - analyze_security_logs : parse auth.log/syslog par regex + détection anomalies.
          - check_ip_reputation   : match IP contre blocklists locales (exact + CIDR).
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import ipaddress
import re
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path

_HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(_HERE.parent))
from _shared.mcp_server import MCPServer  # noqa: E402

server = MCPServer(
    name="cybersec",
    version="1.0.0",
    description_fr="Analyse de logs de sécurité et vérification de réputation IP.",
    description_en="Security log analysis and IP reputation checking.",
)

# ── Patterns de détection ────────────────────────────────────────────────────
_SSH_FAILED = re.compile(
    r"(?P<ts>\w{3}\s+\d+\s+\d{2}:\d{2}:\d{2}).*sshd.*"
    r"Failed password for(?: invalid user)?\s+(?P<user>\S+)\s+"
    r"from\s+(?P<ip>[\d.a-fA-F:]+)"
)
_SSH_ACCEPTED = re.compile(
    r"sshd.*Accepted (?:password|publickey) for\s+(?P<user>\S+)\s+from\s+(?P<ip>[\d.a-fA-F:]+)"
)
_SUDO_CMD = re.compile(
    r"sudo:\s+(?P<user>\S+)\s+:\s+.*COMMAND=(?P<cmd>.+?)(?:\s*$)"
)


@server.tool(
    name="analyze_security_logs",
    description="Détecte les tentatives de brute-force SSH, connexions inhabituelles et escalades sudo dans un log (auth.log/syslog).",
    input_schema={
        "type": "object",
        "properties": {
            "logs": {"type": "string",
                     "description": "Contenu brut du log OU chemin fichier."},
            "brute_force_threshold": {"type": "integer", "default": 5,
                                      "description": "Nb d'échecs pour flag brute-force."},
            "max_lines": {"type": "integer", "default": 5000},
        },
        "required": ["logs"],
    },
)
def analyze_security_logs(args: dict) -> dict:
    raw = args["logs"]
    threshold = int(args.get("brute_force_threshold", 5))
    max_lines = int(args.get("max_lines", 5000))

    # Charge depuis fichier si l'input ressemble à un chemin existant.
    if len(raw) < 500 and Path(raw).is_file():
        try:
            raw = Path(raw).read_text(encoding="utf-8", errors="ignore")
        except Exception:  # noqa: BLE001
            pass

    lines = raw.splitlines()[:max_lines]
    failed_by_ip: dict[str, list[str]] = defaultdict(list)
    accepted_root: list[dict] = []
    sudo_commands: list[dict] = []
    users_seen_failing: set[str] = set()

    for line in lines:
        m = _SSH_FAILED.search(line)
        if m:
            ip = m.group("ip")
            failed_by_ip[ip].append(m.group("user"))
            users_seen_failing.add(m.group("user"))
            continue
        m = _SSH_ACCEPTED.search(line)
        if m and m.group("user") in {"root", "admin"}:
            accepted_root.append({"user": m.group("user"), "ip": m.group("ip")})
            continue
        m = _SUDO_CMD.search(line)
        if m:
            sudo_commands.append({"user": m.group("user"),
                                  "cmd": m.group("cmd")[:120]})

    anomalies: list[dict] = []
    for ip, users in failed_by_ip.items():
        if len(users) >= threshold:
            unique_users = sorted(set(users))
            anomalies.append({
                "type": "ssh_brute_force",
                "source_ip": ip,
                "failed_attempts": len(users),
                "targeted_users": unique_users[:10],
                "severity": "high" if len(users) >= threshold * 3 else "medium",
                "recommendation": (f"Bloquer {ip} au pare-feu (fail2ban, iptables) "
                                   f"et vérifier les comptes ciblés."),
            })
    for ev in accepted_root:
        anomalies.append({
            "type": "root_login_accepted",
            "source_ip": ev["ip"],
            "user": ev["user"],
            "severity": "high",
            "recommendation": ("Interdire la connexion root directe "
                               "(PermitRootLogin no dans sshd_config)."),
        })
    for sc in sudo_commands[:20]:
        if any(dangerous in sc["cmd"] for dangerous in
               ("rm -rf", "chmod 777", "wget ", "curl ", "nc -", "bash -i")):
            anomalies.append({
                "type": "suspicious_sudo",
                "user": sc["user"],
                "command": sc["cmd"],
                "severity": "medium",
                "recommendation": "Auditer cette commande.",
            })

    return {
        "summary": {
            "lines_analyzed": len(lines),
            "unique_source_ips_failing": len(failed_by_ip),
            "total_ssh_failures": sum(len(v) for v in failed_by_ip.values()),
            "sudo_commands_count": len(sudo_commands),
            "anomalies_count": len(anomalies),
        },
        "anomalies": anomalies,
    }


# ── check_ip_reputation ──────────────────────────────────────────────────────
def _load_blocklists() -> list[dict]:
    """Charge toutes les listes de lists/*.txt. Chaque ligne = IP ou CIDR."""
    out = []
    lists_dir = _HERE / "lists"
    if not lists_dir.exists():
        return out
    for txt in sorted(lists_dir.glob("*.txt")):
        entries = []
        for line in txt.read_text(encoding="utf-8", errors="ignore").splitlines():
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            try:
                net = ipaddress.ip_network(line, strict=False)
                entries.append(net)
            except ValueError:
                continue
        out.append({"name": txt.stem, "entries": entries})
    return out


_BLOCKLISTS = _load_blocklists()


@server.tool(
    name="check_ip_reputation",
    description="Vérifie une IP contre les blocklists locales (Tor, malicieuses connues). Match exact + CIDR.",
    input_schema={
        "type": "object",
        "properties": {"ip": {"type": "string",
                              "description": "Adresse IPv4 ou IPv6 à vérifier."}},
        "required": ["ip"],
    },
)
def check_ip_reputation(args: dict) -> dict:
    ip_str = args["ip"].strip()
    try:
        ip = ipaddress.ip_address(ip_str)
    except ValueError as e:
        return {"verdict": "invalid_input", "error": str(e)}

    matches = []
    for bl in _BLOCKLISTS:
        for net in bl["entries"]:
            if ip in net:
                matches.append({"list": bl["name"],
                                "matched_range": str(net)})
                break  # une seule correspondance par liste

    if not matches:
        return {"ip": ip_str, "verdict": "clean", "sources": [], "matches": []}

    verdict = "malicious" if any("bad" in m["list"] or "malware" in m["list"]
                                  for m in matches) else "suspicious"
    return {"ip": ip_str, "verdict": verdict,
            "sources": [m["list"] for m in matches],
            "matches": matches}


if __name__ == "__main__":
    server.run()
