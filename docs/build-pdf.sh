#!/usr/bin/env bash
# Regenerate every PDF distribution artifact next to its Markdown source.
# Markdown is the source of truth. Never edit a PDF.
#
# Covers: docs/technical-guide/**/STANDARD.md
#         docs/SDD.md
#         docs/DELIVERY-PLAN.md
#         docs/api/ENDPOINTS.md
#         docs/USER-MANUAL.md
#         docs/prompt-library/PROMPTS.md  (+ PROMPTS.tr.md, Turkish reading aid)
#         docs/owasp/*.md  (except the index and the link list)
set -euo pipefail
cd "$(dirname "$0")"

command -v pandoc >/dev/null || { echo "pandoc not found — install it, then rerun. PDFs stay pending." >&2; exit 1; }

ENGINE=""
for e in tectonic typst xelatex pdflatex weasyprint; do
  command -v "$e" >/dev/null && ENGINE="$e" && break
done
# Reproducible output: without a fixed epoch every rebuild rewrites all 29 PDFs with a new
# timestamp, so `git status` fills with churn that hides real changes.
export SOURCE_DATE_EPOCH="${SOURCE_DATE_EPOCH:-1704067200}"

[ -n "$ENGINE" ] || { echo "no PDF engine found — install tectonic or typst. PDFs stay pending." >&2; exit 1; }

render() {
  local md="$1" out="${1%.md}.pdf"
  local lang=en
  case "$md" in *.tr.md) lang=tr ;; esac
  echo "  $md -> $out"
  pandoc "$md" -o "$out" \
    --pdf-engine="$ENGINE" \
    -V lang="$lang" \
    --from=markdown+yaml_metadata_block \
    --toc --toc-depth=2 \
    -V geometry:margin=2.5cm \
    -V colorlinks=true -V linkcolor=blue -V urlcolor=blue \
    -V header-includes='\usepackage{xurl}' \
    -V fontsize=10pt
}

find technical-guide -name STANDARD.md -print0 | while IFS= read -r -d '' md; do render "$md"; done
render SDD.md
render DELIVERY-PLAN.md
render api/ENDPOINTS.md
render USER-MANUAL.md
render prompt-library/PROMPTS.md
render prompt-library/PROMPTS.tr.md
for f in owasp/ASVS-CONFORMANCE.md owasp/TOP10-CHECKLIST.md owasp/API-TOP10-CHECKLIST.md owasp/THREAT-REGISTER.md; do
  render "$f"
done

echo "done — engine: $ENGINE"
