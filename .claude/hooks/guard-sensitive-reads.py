#!/usr/bin/env python3
"""PreToolUse guard: require explicit authorization before ANY tool touches secret material.

Why this exists (see docs/kontext-index-poisoning-threat-report.md §5a):
  permissions.deny rules are per-tool — a `Read(...)` deny does not constrain `Bash`
  (`cat ~/.ssh/id_rsa`) or MCP file/exec tools. This hook runs before *every* tool call,
  inspects the tool input regardless of which tool it is, and returns permissionDecision
  "ask" for sensitive targets. "ask" OVERRIDES auto mode, so the human must approve.

Design choices:
  * High-signal targets only (key/cert files, ~/.ssh, cloud/k8s creds, real .env, credential
    stores). Bare words like "secret"/"password" are deliberately NOT matched so editing
    security docs in this repo doesn't nag you into rubber-stamping prompts.
  * Per-token matching so a command mixing `.env.example` (safe) and `~/.ssh/id_rsa` (secret)
    is still caught.
  * Fails OPEN on internal error (never bricks the session) but logs to stderr. Flip
    DECISION to "deny" or FAIL_CLOSED = True below if you want a hard block instead.
"""
import json
import re
import sys

DECISION = "ask"        # "ask" (prompt for authorization) or "deny" (hard block)
FAIL_CLOSED = False     # if True, block when the hook itself errors

SENSITIVE = [
    r"/\.ssh/", r"(^|/)id_rsa\b", r"(^|/)id_ed25519\b", r"(^|/)id_ecdsa\b",
    r"/\.aws/", r"\.aws/credentials", r"/\.config/gcloud\b", r"/\.kube/", r"(^|/)kubeconfig\b",
    r"/\.gnupg/", r"/\.docker/config\.json",
    r"\.git-credentials\b", r"(^|/)\.npmrc\b", r"(^|/)\.pypirc\b", r"(^|/)\.netrc\b", r"(^|/)\.pgpass\b",
    r"/\.claude/\.credentials\.json", r"(^|/)\.claude\.json\b",
    r"\.pem\b", r"\.key\b", r"\.p12\b", r"\.pfx\b", r"\.pkcs12\b", r"\.jks\b", r"\.keystore\b", r"\.ppk\b",
    r"(^|/|\s)\.env(\.[A-Za-z0-9_-]+)?($|\b)",
    r"(^|/)secrets?\.json\b", r"(^|/)credentials?\.json\b",
]

# Sensitive-looking but safe to read; suppresses a match on the SAME token.
ALLOW = [r"\.env\.(example|sample|template|dist)\b", r"\.pub\b"]

TOKEN_SPLIT = re.compile(r"""[\s"',;:|&()<>=]+""")


def is_sensitive(token: str) -> bool:
    if not token:
        return False
    if any(re.search(p, token, re.I) for p in ALLOW):
        return False
    return any(re.search(p, token, re.I) for p in SENSITIVE)


def collect_tokens(tool_name: str, tool_input: dict) -> list[str]:
    # File tools expose a clean path; check it directly.
    path_fields = ("file_path", "path", "absolutePath", "notebook_path")
    tokens: list[str] = []
    for f in path_fields:
        v = tool_input.get(f)
        if isinstance(v, str):
            tokens.append(v)
    # Bash and anything else: tokenize the whole serialized input so MCP tools,
    # shell commands, and unknown arg shapes are all covered.
    blob = tool_input.get("command")
    if not isinstance(blob, str):
        blob = json.dumps(tool_input)
    tokens.extend(TOKEN_SPLIT.split(blob))
    return tokens


def main() -> int:
    try:
        data = json.loads(sys.stdin.read() or "{}")
    except Exception as ex:  # noqa: BLE001
        print(f"guard-sensitive-reads: could not parse hook input: {ex}", file=sys.stderr)
        return 2 if FAIL_CLOSED else 0

    tool_name = data.get("tool_name", "")
    tool_input = data.get("tool_input", {}) or {}

    hits = sorted({t for t in collect_tokens(tool_name, tool_input) if is_sensitive(t)})
    if not hits:
        return 0  # defer to normal permission flow

    reason = (
        f"'{tool_name}' is trying to touch sensitive path(s): {', '.join(hits[:5])}. "
        "Approve only if you intended this — an injected/hijacked agent could be reading secrets."
    )
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": DECISION,
            "permissionDecisionReason": reason,
        }
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
