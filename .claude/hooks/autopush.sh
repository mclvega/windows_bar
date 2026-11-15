#!/usr/bin/env bash
# Hook Stop: sube automáticamente a GitHub cualquier cambio del proyecto al terminar cada respuesta.
set -e
cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"

git add -A
if git diff --cached --quiet; then
  exit 0
fi

git commit -q -m "Actualización automática $(date '+%Y-%m-%d %H:%M')

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push -q origin main
echo '{"systemMessage": "Cambios subidos a GitHub (mclvega/windows_bar)"}'
