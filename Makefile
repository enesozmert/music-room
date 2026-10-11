# The documented interface — ci-cd-containerization.
# First run: git clone -> cp .env.example .env -> fill values -> make up

SOLUTION := MusicRoom/MusicRoom.sln
PERSISTENCE := MusicRoom/src/MusicRoom/Persistence/MusicRoom.Persistence.csproj
WEBAPI := MusicRoom/src/MusicRoom/WebAPI/MusicRoom.WebAPI.csproj

.PHONY: up down migrate test lint load openapi e2e e2e-down agent-check

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

# Gatling runs through its own Maven plugin inside the first-party `maven` image: no official Gatling
# image resolves from this registry, and the one that does is an unmaintained third-party mirror —
# SPEC-023 decision 1. SCENARIO picks one simulation; with none, all five run.
LOAD_IMAGE := maven:3-eclipse-temurin-21
LOAD_SIMULATION_queue-read := musicroom.QueueReadSimulation
LOAD_SIMULATION_vote-storm := musicroom.VoteStormSimulation
LOAD_SIMULATION_reorder    := musicroom.PlaylistReorderSimulation
LOAD_SIMULATION_fan-out    := musicroom.HubFanOutSimulation
LOAD_SIMULATION_login      := musicroom.LoginBurstSimulation
LOAD_SIMULATION_hub-limits := musicroom.HubLimitProbeSimulation

load: ## run a Gatling scenario against the compose stack: make load SCENARIO=vote-storm
	@test -d tests/load/seed/out || { \
		echo "make load: no seed. Run tests/load/seed/seed.sh against a running stack first."; exit 1; }
	docker run --rm --network host \
		-v "$(PWD)/tests/load:/load" -v "$(HOME)/.m2:/root/.m2" \
		-w /load \
		-e MUSICROOM_BASE_URL="$${MUSICROOM_BASE_URL:-http://localhost:8080}" \
		-e MUSICROOM_SEED_DIR=/load/seed/out \
		$(LOAD_IMAGE) mvn -q -B gatling:test \
		$(if $(SCENARIO),-Dgatling.simulationClass=$(LOAD_SIMULATION_$(SCENARIO)),)

# The generator boots the host, which requires DATABASE_URL. This placeholder is not a secret and
# never opens a socket: only a request to /health/ready connects.
OPENAPI_DATABASE_URL := Host=localhost;Port=5432;Database=openapi;Username=openapi;Password=unused

# The host also requires the JWT settings from SPEC-005. These are placeholders on the same footing as the
# connection string above: generating the document signs no token and reaches no network. There is no signing
# key among them — since SPEC-050 every token is RS256, and the key the target generates below is the only
# one the host needs.
OPENAPI_JWT_ISSUER := musicroom-openapi
OPENAPI_JWT_AUDIENCE := musicroom-openapi
OPENAPI_JWT_ACCESS_TOKEN_MINUTES := 15

# The dump runs inside the container: `pg_dump` is not on the host, ADR-0026 keeps it that way, and the
# engine that wrote the files is the one that should read them. The credential is never on the command
# line — `docker compose exec` passes the container its own POSTGRES_USER and POSTGRES_DB — so it reaches
# neither the process table nor the shell history. ADR-0044.
BACKUP_DIR ?= backups
BACKUP_KEEP ?= 7

# Overridable so the refusal below can be proven against a file that names another host. Nothing but a
# test ever passes it — ADR-0044.
BACKUP_ENV_FILE ?= .env

# Prints a pair and writes nothing. This project never generates its own signing key at start-up: a
# generated key is one nobody backed up, nobody can rotate and nothing else can verify against after a
# restart — ADR-0031, issue O19, SPEC-043.
oidc-key: ## print an RS256 private key for OIDC_SIGNING_KEY
	@openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 2>/dev/null \
		| awk 'BEGIN{printf "OIDC_SIGNING_KEY=\""} {printf "%s\\n", $$0} END{print "\""}'
	@echo "# Paste the line above into .env. Rotating: move the old value to OIDC_SIGNING_KEY_PREVIOUS first."

vault: ## start the self-hosted Infisical vault — SPEC-042, ADR-0029
	@test -f .env || { echo "make vault: .env is missing. cp .env.example .env and fill it in — ADR-0026."; exit 1; }
	@set -a; . ./.env; set +a; \
	for name in INFISICAL_ENCRYPTION_KEY INFISICAL_AUTH_SECRET INFISICAL_DB_PASSWORD INFISICAL_REDIS_PASSWORD; do \
		eval "value=\$$$$name"; \
		test -n "$$value" || { echo "make vault: $$name is empty in .env — see .env.example."; exit 1; }; \
	done
	docker compose --profile vault up -d
	@echo "Infisical is at $${INFISICAL_SITE_URL:-http://localhost:8090} once it finishes migrating."

vault-down: ## stop the vault, leaving the application stack running
	docker compose --profile vault down

backup: ## dump the compose database into backups/, keeping the newest BACKUP_KEEP
	@test -f $(BACKUP_ENV_FILE) || { echo "make backup: $(BACKUP_ENV_FILE) is missing. cp .env.example .env and fill it in — ADR-0026."; exit 1; }
	@mkdir -p $(BACKUP_DIR)
	@set -a; . ./$(BACKUP_ENV_FILE); set +a; \
	file="$(BACKUP_DIR)/musicroom-$$(date -u +%Y%m%dT%H%M%SZ).sql"; \
	docker compose exec -T db pg_dump -U "$$POSTGRES_USER" -d "$$POSTGRES_DB" > "$$file"; \
	echo "wrote $$file"
	@ls -1t $(BACKUP_DIR)/musicroom-*.sql 2>/dev/null | tail -n +$$(($(BACKUP_KEEP) + 1)) | \
		while read -r old; do echo "removing $$old"; rm -f "$$old"; done; true

# Refuses anything but the compose database. A restore pointed at Supabase would overwrite rows nobody
# asked it to, and the only thing standing between those two is this check — ADR-0044.
restore: ## restore a dump into the compose database: make restore FILE=backups/<name>.sql
	@test -n "$(FILE)" || { echo "make restore: FILE=backups/<name>.sql is required."; exit 1; }
	@test -f "$(FILE)" || { echo "make restore: $(FILE) does not exist."; exit 1; }
	@test -f $(BACKUP_ENV_FILE) || { echo "make restore: $(BACKUP_ENV_FILE) is missing — ADR-0026."; exit 1; }
	@set -a; . ./$(BACKUP_ENV_FILE); set +a; \
	case "$$DATABASE_URL" in \
		*Host=localhost*|*Host=127.0.0.1*|*Host=db*) ;; \
		*) echo "make restore: DATABASE_URL does not name the compose database. Refusing — ADR-0044."; exit 1;; \
	esac; \
	docker compose exec -T db psql -v ON_ERROR_STOP=1 -U "$$POSTGRES_USER" -d "$$POSTGRES_DB" < "$(FILE)"; \
	echo "restored $(FILE)"

# Both workflows call this target — issue O30, closed by SPEC-048. The generator boots the real host, so
# every start-up requirement has to be satisfied in its environment, and three copies of that list diverge
# the first time one grows: SPEC-043 added OIDC_SIGNING_KEY here, the local gate passed, and CI failed
# because the workflows carried their own copy. SPEC-024's mail guard and SPEC-005's JWT settings had
# already done the same thing. One list, here.
#
# CI passes `--no-restore`, because it restored in an earlier step and a second restore is a network round
# trip for nothing. The default is empty, so a local run behaves as it always did.
OPENAPI_BUILD_FLAGS ?=

openapi: ## regenerate docs/api/openapi.json from the code
	# The generator boots the real host to read the document off it, so every start-up requirement has
	# to be satisfied. The environment is Development because a document build is a development-time
	# operation, and MAIL_PROVIDER is set because SPEC-024 made a mail sender a start-up requirement:
	# `logging` opens no socket, so generating a document sends nothing and needs no provider key.
	#
	# OIDC_SIGNING_KEY is generated here and thrown away: SPEC-043 made a missing key stop the host, and
	# the document describes routes rather than signatures, so nothing this build produces depends on
	# which key it was. A fixed key in this file would be a private key in the repository.
	ASPNETCORE_ENVIRONMENT=Development DATABASE_URL="$(OPENAPI_DATABASE_URL)" \
	MAIL_PROVIDER=logging \
	OIDC_SIGNING_KEY="$$(openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 2>/dev/null)" \
	JWT_ISSUER="$(OPENAPI_JWT_ISSUER)" \
	JWT_AUDIENCE="$(OPENAPI_JWT_AUDIENCE)" \
	JWT_ACCESS_TOKEN_MINUTES="$(OPENAPI_JWT_ACCESS_TOKEN_MINUTES)" \
	dotnet build \
		MusicRoom/src/MusicRoom/WebAPI/MusicRoom.WebAPI.csproj \
		$(OPENAPI_BUILD_FLAGS) -p:GenerateOpenApiDocument=true

CYCLES ?= 3

agent-run: ## drive unattended agent cycles — permissions scoped, output live (CYCLES=n)
	@command -v jq >/dev/null || { echo "jq is required for live output"; exit 1; }
	AGENT_CMD="claude -p --settings .agent/permissions.json --permission-prompts none --output-format stream-json --verbose" \
	AGENT_FILTER="jq -r --unbuffered '\
	  if .type==\"assistant\" then (.message.content[]? | if .type==\"text\" then .text elif .type==\"tool_use\" then \"  -> \"+.name else empty end) \
	  elif .type==\"result\" then \"\\n\"+(.result // \"\") \
	  else empty end'" \
	./.agent/run.sh $(CYCLES)

agy-skills: ## regenerate .agents/skills/ from .claude/commands/ (Antigravity CLI)
	@./.agent/sync-agy-skills.sh

progress: ## count how far the backend has got (WRITE=1 also writes the snapshot)
	@./.agent/progress.sh $(if $(WRITE),--write,)

land: ## land the stack of open pull requests onto main, bottom first (READY=1 to act)
	@./.agent/land-stack.sh $(if $(READY),--ready,--dry-run) $(if $(LIMIT),--limit $(LIMIT),)

# The end-to-end check — SPEC-046. It composes the overlay on top of the ordinary stack, so what it
# drives is the real runtime: Kestrel, PostgreSQL, the middleware order and the configuration a
# container actually receives. Three things come from here rather than from .env, each for a reason
# written in docker-compose.e2e.yml.
#
# The signing key is generated per run and thrown away, with its newlines escaped because that is the
# shape `.env`, compose and a hosting platform's secret field all carry cleanly — the provider undoes
# the escape, and SPEC-043's test covers that.
E2E_COMPOSE := docker compose -f docker-compose.yml -f docker-compose.e2e.yml

e2e: ## drive the whole identity surface against the composed stack
	@test -f .env || { echo "make e2e: .env is missing. cp .env.example .env and fill it in — ADR-0026."; exit 1; }
	OIDC_SIGNING_KEY="$$(openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 2>/dev/null | awk 'BEGIN { ORS = "\\n" } 1')" \
	$(E2E_COMPOSE) up -d --build
	$(MAKE) migrate
	./scripts/e2e-oidc.sh

e2e-down: ## stop the end-to-end stack
	$(E2E_COMPOSE) down

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
