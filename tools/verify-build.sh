#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════
# Weryfikacja zmian w kontenerze .NET SDK — NIE dotyka bazy danych.
#
# Uruchamia build, testy i kontrolę spójności migracji wewnątrz obrazu
# mcr.microsoft.com/dotnet/sdk:10.0. Na Unraid (albo dowolnym hoście z
# Dockerem) wystarczy mieć kod w bieżącym katalogu i wpisać:
#
#     bash tools/verify-build.sh
#
# Nic nie instaluje na stałe — kontener znika po zakończeniu (--rm).
# ═══════════════════════════════════════════════════════════════════════════
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
echo "Katalog projektu: $REPO_ROOT"
echo "Uruchamiam w kontenerze .NET SDK 10 (pierwszy raz pobierze obraz ~700 MB)..."
echo ""

docker run --rm \
  -v "$REPO_ROOT":/src \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c '
    set -e
    echo "════ 1/4 · restore ════"
    dotnet restore PTScheduler.Web.slnx

    echo ""
    echo "════ 2/4 · build (Release) ════"
    dotnet build PTScheduler.Web.slnx -c Release --no-restore

    echo ""
    echo "════ 3/4 · testy ════"
    dotnet test PTScheduler.Tests/PTScheduler.Tests.csproj -c Release --no-build

    echo ""
    echo "════ 4/4 · kontrola spójności migracji ════"
    echo "Instaluję dotnet-ef..."
    dotnet tool install --global dotnet-ef --version 10.* >/dev/null 2>&1 || true
    export PATH="$PATH:/root/.dotnet/tools"

    # Generuje kandydata na migrację i sprawdza, czy jest PUSTA.
    # Pusta = snapshot zgadza się z modelem, czyli ręcznie dopisana migracja
    # StartTime jest kompletna. Niepusta = coś się nie zgadza.
    dotnet ef migrations add __VerifyEmpty \
      --project PTScheduler.Infrastructure \
      --startup-project PTScheduler.Web \
      --no-build 2>/dev/null || dotnet ef migrations add __VerifyEmpty \
      --project PTScheduler.Infrastructure \
      --startup-project PTScheduler.Web

    UP_FILE=$(ls -t PTScheduler.Infrastructure/Migrations/*__VerifyEmpty.cs 2>/dev/null | head -1)
    if [ -z "$UP_FILE" ]; then
      echo "⚠ Nie znaleziono wygenerowanej migracji — sprawdź ręcznie."
      exit 1
    fi

    # Liczymy linie z faktyczną operacją w metodzie Up (migrationBuilder.*).
    OPS=$(grep -c "migrationBuilder\." "$UP_FILE" || true)
    echo ""
    echo "Wygenerowana migracja: $UP_FILE"
    echo "Operacji w Up(): $OPS"

    # Sprzątamy — to była tylko kontrola.
    dotnet ef migrations remove \
      --project PTScheduler.Infrastructure \
      --startup-project PTScheduler.Web \
      --no-build 2>/dev/null || rm -f "${UP_FILE%.cs}"*.cs

    echo ""
    if [ "$OPS" -eq 0 ]; then
      echo "✓ Migracja StartTime jest kompletna (kandydat pusty)."
    else
      echo "⚠ Kandydat NIE jest pusty — snapshot rozjechany, zgłoś to."
    fi
  '

echo ""
echo "═══════════════════════════════════════════════════════════════"
echo "Jeśli wszystkie 4 kroki przeszły na zielono — kod jest OK do wdrożenia."
echo "Migracja na produkcji to OSOBNY krok: najpierw backup + tz-diagnostic.sql."
echo "═══════════════════════════════════════════════════════════════"
