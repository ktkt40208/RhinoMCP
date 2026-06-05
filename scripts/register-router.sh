#!/usr/bin/env bash
# Register (or remove) the locally-built dev router as an MCP server in both
# Claude Code and Codex (~/.codex/config.toml).
# Run by hand after building the router — not part of the build.
#
#   ./scripts/register-router.sh            # (re)register: remove then add
#   ./scripts/register-router.sh -v 8       # register with --default-version 8
#   ./scripts/register-router.sh remove     # unregister
set -euo pipefail

NAME="rhino-mcp-router-dev"
VERSION="WIP"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BIN="$ROOT/rhino/router/bin/R9/Debug/net8.0/rhino-mcp-router"

CMD="add"
while [ $# -gt 0 ]; do
  case "$1" in
    -v|--version) VERSION="$2"; shift 2 ;;
    add|remove)   CMD="$1"; shift ;;
    *) echo "usage: $0 [add|remove] [-v VERSION]" >&2; exit 1 ;;
  esac
done

case "$CMD" in
  remove)
    claude mcp remove "$NAME" -s user
    codex mcp remove "$NAME"
    ;;
  add)
    [ -x "$BIN" ] || { echo "router not built: $BIN" >&2; echo "build it first: dotnet build rhino/router/Router.csproj" >&2; exit 1; }
    claude mcp remove "$NAME" -s user >/dev/null 2>&1 || true
    claude mcp add "$NAME" -s user "$BIN" -- --default-version "$VERSION"
    codex mcp remove "$NAME" >/dev/null 2>&1 || true
    codex mcp add "$NAME" -- "$BIN" --default-version "$VERSION"
    ;;
esac
