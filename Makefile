# The documented interface — ci-cd-containerization.
# First run: git clone -> cp .env.example .env -> fill values -> make up

SOLUTION := MusicRoom/MusicRoom.sln
PERSISTENCE := MusicRoom/src/MusicRoom/Persistence/MusicRoom.Persistence.csproj
WEBAPI := MusicRoom/src/MusicRoom/WebAPI/MusicRoom.WebAPI.csproj

.PHONY: up down migrate test lint load openapi agent-check

up: ## build and start the full stack
	docker compose up -d --build

down: ## stop and remove containers
	docker compose down

# `dotnet ef` runs on the host, so it takes DATABASE_URL from .env, which names localhost. The
# compose file rewrites the same value to Host=db for the container. Sourcing .env keeps the
# credential out of the command line and out of this file — secrets-management, ADR-0026.
migrate: ## apply EF Core migrations to the database DATABASE_URL names
	@test -f .env || { echo "make migrate: .env is missing. cp .env.example .env and fill it in — ADR-0026."; exit 1; }
	dotnet tool restore
	@set -a; . ./.env; set +a; \
	dotnet ef database update \
		--project $(PERSISTENCE) \
		--startup-project $(WEBAPI) \
		--context MusicRoomDbContext

test: ## run the full test suite
	dotnet test --solution $(SOLUTION)

lint: ## formatting is the single .editorconfig authority
	dotnet format $(SOLUTION) --verify-no-changes

load: ## run the load scenario
	@echo "make load: no simulation yet. Gatling scenarios arrive with slice S9 — see load-testing."

# The generator boots the host, which requires DATABASE_URL. This placeholder is not a secret and
# never opens a socket: only a request to /health/ready connects.
OPENAPI_DATABASE_URL := Host=localhost;Port=5432;Database=openapi;Username=openapi;Password=unused

# The host also requires the JWT settings from SPEC-005, and refuses a signing key under 32 bytes.
# These are placeholders on the same footing as the connection string above: generating the document
# signs no token and reaches no network. Never reuse them anywhere a real token is issued.
OPENAPI_JWT_SIGNING_KEY := openapi-document-build-placeholder-key-not-a-secret
OPENAPI_JWT_ISSUER := musicroom-openapi
OPENAPI_JWT_AUDIENCE := musicroom-openapi
OPENAPI_JWT_ACCESS_TOKEN_MINUTES := 15

openapi: ## regenerate docs/api/openapi.json from the code
	# The generator boots the real host to read the document off it, so the environment must be
	# Development: the SPEC-003 start-up guard refuses an IMailService anywhere else, and a
	# document build is a development-time operation.
	ASPNETCORE_ENVIRONMENT=Development DATABASE_URL="$(OPENAPI_DATABASE_URL)" \
	JWT_SIGNING_KEY="$(OPENAPI_JWT_SIGNING_KEY)" \
	JWT_ISSUER="$(OPENAPI_JWT_ISSUER)" \
	JWT_AUDIENCE="$(OPENAPI_JWT_AUDIENCE)" \
	JWT_ACCESS_TOKEN_MINUTES="$(OPENAPI_JWT_ACCESS_TOKEN_MINUTES)" \
	dotnet build \
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

progress: ## count how far the backend has got (WRITE=1 also writes the snapshot)
	@./.agent/progress.sh $(if $(WRITE),--write,)

land: ## land the stack of open pull requests onto main, bottom first (READY=1 to act)
	@./.agent/land-stack.sh $(if $(READY),--ready,--dry-run) $(if $(LIMIT),--limit $(LIMIT),)

agent-check: ## the full local gate; every spec's Verification section calls this
	dotnet restore $(SOLUTION)
	dotnet format $(SOLUTION) --verify-no-changes
	dotnet build $(SOLUTION) -warnaserror --no-restore
	dotnet test --solution $(SOLUTION) --no-build
	dotnet list $(SOLUTION) package --vulnerable --include-transitive
	$(MAKE) openapi
	git diff --exit-code -- docs/api/openapi.json
	# Same guard for the distribution PDFs: Markdown is the source of truth, so a PDF that does
	# not match its source is a stale artifact shipped to a reader. SOURCE_DATE_EPOCH keeps the
	# output byte-stable, so any diff here is a real content difference.
	./docs/build-pdf.sh >/dev/null
	git diff --exit-code -- '*.pdf'
