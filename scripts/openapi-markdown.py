#!/usr/bin/env python3
"""Render docs/api/openapi.json as Markdown — SPEC-060.

The generated document is a reference a reader can hold: every route the code declares, with its
parameters, its request body, its answers and the schemas behind them. It is produced from the same
file a generated client is produced from, so it cannot drift from the API while saying it does not.

`docs/api/ENDPOINTS.md` stays the contract: it says what a route *means*, why it answers what it
answers, and which rule decided that. This says what is on the wire. One is argued, the other is
derived, and neither replaces the other.

Usage: scripts/openapi-markdown.py docs/api/openapi.json docs/api/API-REFERENCE.md
"""

from __future__ import annotations

import json
import sys
from typing import Any

METHODS = ("get", "post", "put", "patch", "delete", "head", "options", "trace")


def type_of(schema: dict[str, Any] | None) -> str:
    """One readable type for a schema, following `$ref` by name only."""
    if not schema:
        return "—"

    if "$ref" in schema:
        return f"`{schema['$ref'].rsplit('/', 1)[-1]}`"

    if "items" in schema:
        return f"array of {type_of(schema['items'])}"

    # `anyOf` is how a nullable value arrives; the null member is noise in a table.
    for key in ("anyOf", "oneOf", "allOf"):
        if key in schema:
            members = [m for m in schema[key] if m.get("type") != "null"]
            rendered = ", ".join(type_of(m) for m in members) or "—"
            return rendered + (" or null" if len(members) != len(schema[key]) else "")

    kind = schema.get("type", "—")
    fmt = schema.get("format")

    return f"`{kind}` ({fmt})" if fmt else f"`{kind}`"


def escape(text: str) -> str:
    """Keep a cell inside its column: a pipe in prose would start a new one."""
    return text.replace("|", "\\|").replace("\n", " ").strip()


def body_type(operation: dict[str, Any]) -> str:
    """The request body's schema, or a dash."""
    content = operation.get("requestBody", {}).get("content", {})
    json_body = content.get("application/json")

    return type_of(json_body.get("schema")) if json_body else "—"


def parameters(operation: dict[str, Any]) -> list[str]:
    """One line per parameter, grouped by where it travels."""
    lines: list[str] = []

    for parameter in operation.get("parameters", []):
        required = "required" if parameter.get("required") else "optional"
        lines.append(
            f"- `{parameter['name']}` in {parameter.get('in', '?')}, {required} — "
            f"{type_of(parameter.get('schema'))}"
        )

    return lines


def responses(operation: dict[str, Any]) -> list[str]:
    """Status codes in numeric order, with what each carries."""
    rows: list[str] = []

    for status, response in sorted(operation.get("responses", {}).items()):
        content = response.get("content", {}).get("application/json")
        carries = type_of(content.get("schema")) if content else "no body"
        rows.append(f"| `{status}` | {escape(response.get('description', ''))} | {carries} |")

    return rows


def operation_section(path: str, method: str, operation: dict[str, Any]) -> list[str]:
    """One route, as a reader meets it."""
    summary = operation.get("summary") or operation.get("operationId") or ""
    out = [f"### `{method.upper()} {path}`", ""]

    if summary:
        out += [f"**{summary}**", ""]

    if description := operation.get("description"):
        out += [description, ""]

    if params := parameters(operation):
        out += ["Parameters:", ""] + params + [""]

    if (request := body_type(operation)) != "—":
        out += [f"Request body: {request}", ""]

    out += ["| Status | Meaning | Body |", "|---|---|---|"] + responses(operation) + [""]

    return out


def schema_section(name: str, schema: dict[str, Any]) -> list[str]:
    """One schema and its properties."""
    out = [f"### `{name}`", ""]

    if description := schema.get("description"):
        out += [escape(description), ""]

    properties = schema.get("properties")

    if not properties:
        out += [f"Type: {type_of(schema)}", ""]

        return out

    required = set(schema.get("required", []))
    out += ["| Property | Type | Required |", "|---|---|---|"]

    for property_name, property_schema in properties.items():
        mandatory = "yes" if property_name in required else "no"
        out.append(f"| `{property_name}` | {type_of(property_schema)} | {mandatory} |")

    return out + [""]


def render(document: dict[str, Any]) -> str:
    """The whole reference, deterministic from the document's own order."""
    info = document.get("info", {})
    out = [
        "---",
        f"title: \"{info.get('title', 'API reference')}\"",
        f"subtitle: \"Generated from docs/api/openapi.json — version {info.get('version', '')}\"",
        "---",
        "",
        "# API reference",
        "",
        "Generated from `docs/api/openapi.json`, which is generated from the code and guarded against",
        "drift by `make agent-check`. **Nothing here is written by hand**, so nothing here can be out of",
        "date with the API while claiming otherwise.",
        "",
        "It says what is on the wire. What a route *means* — which rule decided its status codes, why a",
        "refusal says what it says — is `docs/api/ENDPOINTS.md`, and that document is the contract.",
        "",
        "## Routes",
        "",
    ]

    for path, item in document.get("paths", {}).items():
        for method in METHODS:
            if method in item:
                out += operation_section(path, method, item[method])

    if schemas := document.get("components", {}).get("schemas", {}):
        out += ["## Schemas", ""]

        for name, schema in schemas.items():
            out += schema_section(name, schema)

    return "\n".join(out) + "\n"


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__, file=sys.stderr)

        return 2

    source, target = sys.argv[1], sys.argv[2]

    with open(source, encoding="utf-8") as handle:
        document = json.load(handle)

    with open(target, "w", encoding="utf-8") as handle:
        handle.write(render(document))

    paths = sum(
        1 for item in document.get("paths", {}).values() for method in METHODS if method in item
    )
    print(f"{target}: {paths} operations")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
