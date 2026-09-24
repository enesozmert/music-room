# The documented interface — ci-cd-containerization.
# First run: git clone -> cp .env.example .env -> fill values -> make up

SOLUTION := MusicRoom/MusicRoom.sln

.PHONY: up down migrate test lint load openapi agent-check

up: ## build and start the full stack
	docker compose up -d --build

down: ## stop and remove containers
	docker compose down

migrate: ## apply EF Core migrations
	@echo "make migrate: nothing to apply. The first migration arrives with slice S1 (Identity)."
	@echo "Until then there is no DbContext and no table — SPEC-001 out of scope."

test: ## run the full test suite
	dotnet test --solution $(SOLUTION)

lint: ## formatting is the single .editorconfig authority
	dotnet format $(SOLUTION) --verify-no-changes

load: ## run the load scenario
	@echo "make load: no simulation yet. Gatling scenarios arrive with slice S9 — see load-testing."

# The generator boots the host, which requires DATABASE_URL. This placeholder is not a secret and
# never opens a socket: only a request to /health/ready connects.
OPENAPI_DATABASE_URL := Host=localhost;Port=5432;Database=openapi;Username=openapi;Password=unused

openapi: ## regenerate docs/api/openapi.json from the code
	DATABASE_URL="$(OPENAPI_DATABASE_URL)" dotnet build \
		MusicRoom/src/MusicRoom/WebAPI/MusicRoom.WebAPI.csproj \
		-p:GenerateOpenApiDocument=true

CYCLES ?= 3

agent-run: ## drive unattended agent cycles — permissions scoped, output live (CYCLES=n)
	@command -v jq >/dev/null || { echo "jq is required for live output"; exit 1; }
	AGENT_CMD="claude -p --settings .agent/permissions.json --permission-prompts none --output-format stream-json --verbose" \
	AGENT_FILTER="jq -r --unbuffered '\
	  if .type==\"assistant\" then (.message.content[]? | if .type==\"text\" then .text elif .type==\"tool_use\" then \"  -> \"+.name else empty end) \
	  elif .type==\"result\" then \"\\n\"+(.result // \"\") \
	  else empty end'" \
	./.agent/run.sh $(CYCLES)

agent-check: ## the full local gate; every spec's Verification section calls this
	dotnet restore $(SOLUTION)
	dotnet format $(SOLUTION) --verify-no-changes
	dotnet build $(SOLUTION) -warnaserror --no-restore
	dotnet test --solution $(SOLUTION) --no-build
	dotnet list $(SOLUTION) package --vulnerable --include-transitive
	$(MAKE) openapi
	git diff --exit-code -- docs/api/openapi.json
